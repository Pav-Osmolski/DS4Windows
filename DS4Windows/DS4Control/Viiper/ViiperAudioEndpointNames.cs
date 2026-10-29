using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;

namespace DS4Windows
{
    // Cosmetic Windows endpoint names only. Never change USB identities, endpoint
    // IDs, default devices, device visibility, volume, or the physical controller.
    internal sealed class ViiperAudioEndpointNames : IDisposable
    {
        internal sealed record Endpoint(string Id, string Name, DataFlow Flow, int Port);

        private readonly ManualResetEvent stop = new(false);
        private readonly object writeGate = new();
        private bool retired;
        private bool finished;
        private readonly Func<bool> isCurrent;
        private readonly int port;
        private readonly int controllerIndex;

        internal ViiperAudioEndpointNames(int port, int controllerIndex, Func<bool> isCurrent)
        {
            this.port = port;
            this.controllerIndex = controllerIndex;
            this.isCurrent = isCurrent;
            var worker = new Thread(Run) { IsBackground = true, Name = "VIIPER audio endpoint names" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        internal static string NameFor(int controllerIndex, DataFlow flow)
        {
            if (controllerIndex < 0 || controllerIndex >= ControlService.MAX_DS4_CONTROLLER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(controllerIndex));
            return flow switch
            {
                DataFlow.Capture => $"DS4Windows Controller {controllerIndex + 1} Microphone",
                DataFlow.Render => $"DS4Windows Controller {controllerIndex + 1} Audio",
                _ => throw new ArgumentOutOfRangeException(nameof(flow)),
            };
        }

        // Matching is by the owned USB/IP ancestor, never by a friendly name or
        // Sony VID/PID (which are shared by real and emulated controllers).
        internal static int ApplyNames(IEnumerable<Endpoint> endpoints, int ownedPort,
            int controllerIndex, Func<bool> isCurrent, Action<string, string> rename)
        {
            int flows = 0;
            if (ownedPort <= 0) return flows;
            foreach (Endpoint endpoint in endpoints)
            {
                if (!isCurrent()) break;
                if (endpoint.Port != ownedPort || string.IsNullOrEmpty(endpoint.Id) ||
                    endpoint.Flow is not (DataFlow.Render or DataFlow.Capture)) continue;
                string name = NameFor(controllerIndex, endpoint.Flow);
                if (!string.Equals(endpoint.Name, name, StringComparison.Ordinal))
                {
                    if (!isCurrent()) break;
                    rename(endpoint.Id, name);
                }
                flows |= endpoint.Flow == DataFlow.Render ? 1 : 2;
            }
            return flows;
        }

        private void Run()
        {
            try { RunCore(); }
            finally
            {
                lock (writeGate)
                {
                    finished = true;
                    stop.Dispose();
                }
            }
        }

        private void RunCore()
        {
            Exception lastError = null;
            // Windows audio endpoints appear after the virtual USB device. Retry
            // off the input/UI threads; a naming failure must not stop a pad.
            for (int attempt = 0; attempt < 60 && !stop.WaitOne(0) && isCurrent(); attempt++)
            {
                try
                {
                    var endpoints = new List<Endpoint>();
                    using (var enumerator = new MMDeviceEnumerator())
                    {
                        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(
                            DataFlow.All, DeviceState.Active | DeviceState.Unplugged))
                        {
                            using (device)
                            {
                                if (DualSenseAudioPassthrough.TryGetEndpointUsbipPort(device, out int endpointPort)
                                    && endpointPort == port)
                                    endpoints.Add(new Endpoint(device.ID, device.Properties[PropertyKeys.PKEY_Device_DeviceDesc]?.Value?.ToString() ?? device.FriendlyName,
                                        device.DataFlow, endpointPort));
                            }
                        }
                    }
                    if (ApplyNames(endpoints, port, controllerIndex,
                        () => !stop.WaitOne(0) && isCurrent(), (id, name) =>
                        {
                            lock (writeGate)
                            {
                                if (!retired && isCurrent()) Rename(id, name);
                            }
                        }) == 3) return;
                }
                catch (Exception ex) { lastError = ex; }
                if (stop.WaitOne(500)) return;
            }
            if (!stop.WaitOne(0) && isCurrent() && lastError != null)
                AppLogger.LogToGui($"Virtual controller audio names could not be updated: {lastError.Message}", false);
        }

        internal static void Rename(string endpointId, string name)
        {
            // Windows' audio-policy client supports the same endpoint-label edit
            // as Sound settings for standard users. IMMDevice's writable store
            // alone requires elevation. Isolate this Windows compatibility API:
            // errors are reported by the worker without affecting audio.
            // DeviceDesc is the editable label. FriendlyName (property 14) is
            // synthesized by Windows with the adapter suffix and is protected.
            object client = new PolicyConfigClient();
            IntPtr text = Marshal.StringToCoTaskMemUni(name);
            try
            {
                var key = PropertyKeys.PKEY_Device_DeviceDesc;
                var value = new StringVariant { Type = 31, Text = text }; // VT_LPWSTR
                Marshal.ThrowExceptionForHR(((IPolicyConfig)client).SetPropertyValue(
                    endpointId, false, ref key, ref value));
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
                if (Marshal.IsComObject(client)) Marshal.ReleaseComObject(client);
            }
        }

        public void Dispose()
        {
            // Fence only the short property write, not Windows enumeration or
            // the retry delay. A slow PnP read must not hold up Stop or rebind.
            lock (writeGate)
            {
                retired = true;
                if (!finished) stop.Set();
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct StringVariant
        {
            [FieldOffset(0)] internal ushort Type;
            [FieldOffset(8)] internal IntPtr Text;
        }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class PolicyConfigClient { }

        // Windows 10/11 IPolicyConfig ABI (with the FX-store selector), as
        // used by SoundSwitch. Do not use the older three-argument ABI.
        // Only SetPropertyValue is invoked; preceding slots retain COM order.
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
            InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            void Unused1();
            void Unused2();
            void Unused3();
            void Unused4();
            void Unused5();
            void Unused6();
            void Unused7();
            void Unused8();
            void Unused9();
            [PreserveSig] int SetPropertyValue(
                [MarshalAs(UnmanagedType.LPWStr)] string endpointId,
                [MarshalAs(UnmanagedType.Bool)] bool fxStore, ref PropertyKey key, ref StringVariant value);
        }
    }
}
