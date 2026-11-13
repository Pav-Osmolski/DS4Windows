using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DS4Windows;

// One short, one-shot decision per playback role. Never writes capture or
// communications defaults. Existing endpoints and later choices are left alone.
internal sealed class PlaybackProtectionWindow
{
    internal const int ArrivalWindowMilliseconds = 2000;
    internal const int StartupWindowMilliseconds = 30000;
    private readonly Dictionary<Role, string> previous;
    private readonly HashSet<string> existing;
    private readonly HashSet<Role> finished = new();
    private readonly long started;

    internal PlaybackProtectionWindow(Dictionary<Role, string> previous,
        IEnumerable<string> existing, long started)
    {
        this.previous = previous;
        this.existing = new(existing, StringComparer.OrdinalIgnoreCase);
        this.started = started;
    }

    internal string Consider(Role role, string observed, string current,
        bool owned, bool previousActive, long arrival, long changed, long now)
    {
        if (changed < started || role is not (Role.Console or Role.Multimedia) || finished.Contains(role) ||
            !previous.TryGetValue(role, out string before) || string.IsNullOrEmpty(before) ||
            string.Equals(before, observed, StringComparison.OrdinalIgnoreCase)) return null;
        // Default notification may arrive before the device-added notification.
        // Wait for ownership's arrival evidence rather than consuming the role.
        if (owned && arrival < started && !existing.Contains(observed ?? "")) return null;
        // A different user/device choice cancels this role, including a change
        // which has already superseded the queued notification.
        finished.Add(role);
        if (!owned || !previousActive || existing.Contains(observed ?? "") ||
            !string.Equals(current, observed, StringComparison.OrdinalIgnoreCase) ||
            now - started > StartupWindowMilliseconds || changed < started ||
            arrival < started || changed - arrival < -250 ||
            changed - arrival > ArrivalWindowMilliseconds || now - changed > ArrivalWindowMilliseconds)
            return null;
        return before;
    }
}

// Windows audio COM lives on a dedicated MTA worker. Notification-driven: no
// endpoint enumeration on a UI/input timer, and no writes on a feedback lane.
internal static class WindowsPlaybackProtection
{
    internal sealed record Output(string Name, int Port, int ControllerIndex);
    private sealed record Change(string Id, long Time);
    internal sealed class Lease : IDisposable
    {
        internal PlaybackProtectionWindow Window;
        internal int Port;
        internal int ControllerIndex;
        internal Func<bool> IsCurrent = () => true;
        internal int Retired;
        internal void Bind(int port, int index, Func<bool> isCurrent) => Queue(e =>
        {
            Port = port;
            ControllerIndex = index;
            IsCurrent = isCurrent;
            Reconcile(e);
        });
        public void Dispose()
        {
            lock (this) Interlocked.Exchange(ref Retired, 1);
            Queue(e => { leases.Remove(this); PublishOutput(e); });
        }
    }

    private static readonly BlockingCollection<Action<MMDeviceEnumerator>> work = new();
    private static readonly List<Lease> leases = new(); // worker only
    private static readonly Dictionary<string, long> arrivals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Role, Change> changes = new();
    private static readonly Role[] roles = { Role.Console, Role.Multimedia };
    private static Output output;
    private static int started;
    private static int stopped;
    internal static Output CurrentOutput => Volatile.Read(ref output);
    internal static event EventHandler OutputChanged;
    internal static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        var thread = new Thread(Run) { IsBackground = true, Name = "Windows playback protection" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        Queue(PublishOutput);
    }

    internal static Lease BeforeVirtualDeviceCreation()
    {
        EnsureStarted();
        var lease = new Lease();
        // Capture BEFORE asking VIIPER to enumerate the new USB device. A slow
        // or unavailable Windows audio service fails open; it cannot block input.
        using var ready = new ManualResetEventSlim();
        Queue(e =>
        {
            if (Volatile.Read(ref lease.Retired) != 0) return;
            try
            {
                if (!Global.PreserveWindowsAudioOutput)
                {
                    leases.Add(lease); // Ownership still supports routing guidance.
                    return;
                }
                var defaults = new Dictionary<Role, string>();
                foreach (Role role in roles)
                {
                    using var device = GetDefault(e, role);
                    if (device != null && !DualSenseAudioPassthrough.TryGetEndpointUsbipPort(device, out _))
                        defaults[role] = device.ID;
                }
                var existing = new List<string>();
                foreach (MMDevice device in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    using (device) existing.Add(device.ID);
                lease.Window = new(defaults, existing, Environment.TickCount64);
                leases.Add(lease);
            }
            finally
            {
                // The caller can time out without disposing a handle still in use.
                lock (lease) { if (lease.Retired == 0) ready.Set(); }
            }
        });
        if (ready.Wait(500)) return lease;
        lock (lease) Interlocked.Exchange(ref lease.Retired, 1);
        Queue(e => leases.Remove(lease));
        return null;
    }

    internal static void Shutdown()
    {
        Interlocked.Exchange(ref stopped, 1);
        work.CompleteAdding();
    }

    private static void Queue(Action<MMDeviceEnumerator> action)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        try { work.Add(action); }
        catch (InvalidOperationException) { /* Exit raced an audio notification. */ }
    }
    private static void Run()
    {
        // Keep the worker alive if the audio service is unavailable at startup;
        // a later request can retry without preventing controllers from starting.
        MMDeviceEnumerator enumerator = null;
        var notifications = new Notifications();
        try
        {
            foreach (var action in work.GetConsumingEnumerable())
            {
                if (Volatile.Read(ref stopped) != 0) break;
                try
                {
                    if (enumerator == null)
                    {
                        var candidate = new MMDeviceEnumerator();
                        try { candidate.RegisterEndpointNotificationCallback(notifications); }
                        catch { candidate.Dispose(); throw; }
                        enumerator = candidate;
                    }
                    action(enumerator);
                }
                catch (Exception error) { TraceFailure(error); }
            }
        }
        finally
        {
            if (enumerator != null)
            {
                try { enumerator.UnregisterEndpointNotificationCallback(notifications); }
                catch { /* Windows audio can already be shutting down. */ }
                try { enumerator.Dispose(); }
                catch { /* A shutdown failure must not crash the app. */ }
            }
        }
    }

    private static MMDevice GetDefault(MMDeviceEnumerator e, Role role)
    {
        try { return e.GetDefaultAudioEndpoint(DataFlow.Render, role); }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    private static bool Active(MMDeviceEnumerator e, string id)
    {
        try { using var device = e.GetDevice(id); return device.State == DeviceState.Active; }
        catch { return false; }
    }

    private static void Reconcile(MMDeviceEnumerator e)
    {
        foreach (var lease in leases.ToArray())
        {
            if (lease.Retired != 0 || lease.Port <= 0 || lease.Window == null || !lease.IsCurrent()) continue;
            foreach (Role role in roles)
            {
                if (!changes.TryGetValue(role, out var change)) continue;
                using var current = GetDefault(e, role);
                bool owned = current != null &&
                    DualSenseAudioPassthrough.TryGetEndpointUsbipPort(current, out int port) && port == lease.Port;
                arrivals.TryGetValue(change.Id ?? "", out long arrival);
                string restore = lease.Window.Consider(role, change.Id, current?.ID, owned,
                    true, arrival, change.Time, Environment.TickCount64);
                if (restore == null || !Active(e, restore) || lease.Retired != 0 ||
                    !Global.PreserveWindowsAudioOutput || !lease.IsCurrent()) continue;
                // Re-read immediately before the write; a queued event must never
                // overwrite a newer default choice. Consume each role only once.
                using var latest = GetDefault(e, role);
                if (!string.Equals(latest?.ID, change.Id, StringComparison.OrdinalIgnoreCase)) continue;
                lock (lease)
                {
                    if (lease.Retired != 0 || !Global.PreserveWindowsAudioOutput || !lease.IsCurrent()) continue;
                    ViiperAudioEndpointNames.SetDefaultPlaybackEndpoint(restore, role);
                }
                if (Global.VerboseStartupLogging)
                    AppLogger.LogToGui($"Windows playback output preserved: role={role}; virtual USB/IP port={lease.Port}.", false);
            }
        }
        PublishOutput(e);
    }

    private static void PublishOutput(MMDeviceEnumerator e)
    {
        using var device = GetDefault(e, Role.Multimedia);
        int port = 0;
        if (device != null) DualSenseAudioPassthrough.TryGetEndpointUsbipPort(device, out port);
        int index = leases.FirstOrDefault(l => l.Retired == 0 && l.Port == port && port > 0 && l.IsCurrent())?.ControllerIndex ?? -1;
        var next = new Output(device?.FriendlyName ?? "", port, index);
        if (next == CurrentOutput) return;
        Volatile.Write(ref output, next);
        OutputChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void TraceFailure(Exception error)
    {
        if (Global.VerboseStartupLogging)
            AppLogger.LogToGui($"Windows playback protection unavailable: {error.Message}", false);
    }

    private sealed class Notifications : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
        {
            if (flow != DataFlow.Render || role == Role.Communications) return;
            long now = Environment.TickCount64;
            Queue(e => { changes[role] = new(id, now); Reconcile(e); });
        }
        public void OnDeviceAdded(string id) => Arrived(id);
        public void OnDeviceStateChanged(string id, DeviceState state)
        {
            if (state == DeviceState.Active) Arrived(id);
            else Queue(Reconcile);
        }
        private static void Arrived(string id)
        {
            long now = Environment.TickCount64;
            Queue(e => { arrivals[id] = now; Reconcile(e); });
        }
        public void OnDeviceRemoved(string id) => Queue(e => { arrivals.Remove(id); Reconcile(e); });
        public void OnPropertyValueChanged(string id, PropertyKey key) => Queue(PublishOutput);
    }
}
