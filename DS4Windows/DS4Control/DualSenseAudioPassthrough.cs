using NAudio.CoreAudioApi;
using NAudio.Wave;
using DS4Windows.InputDevices;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DS4Windows
{
    internal enum ControllerAudioEndpointKind
    {
        Any,
        DualShock4,
        DualSense,
    }

    internal enum DirectSpeakerRouteDecision
    {
        Loopback,
        Direct,
        Pending,
    }

    internal enum DirectSpeakerEndpointOwnership
    {
        Unresolved,
        Owned,
        Unowned,
        Missing,
    }

    public sealed class DualSenseAudioPassthrough : IDisposable
    {
        public const string AutoDetectGameAudioEndpointId = "DS4Windows:AutoDetectDualSenseGameAudio";
        public const string DefaultSystemAudioEndpointId = "DS4Windows:DefaultSystemAudio";

        private const string EndpointHistoryValueName = "{4b416b7d-8501-40c1-acfd-97aa9bdc17c8},1";
        private const string RenderEndpointRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\";
        private const int ControllerCount = ControlService.MAX_DS4_CONTROLLER_COUNT;
        internal const int BluetoothStartRetryAttempts = 240;
        internal const int BluetoothStartRetryDelayMilliseconds = 250;
        private readonly object syncRoot = new object();
        private readonly SlotPlayback[] slots = new SlotPlayback[ControllerCount];
        private readonly DualSenseBluetoothSpeakerPassthrough[] bluetoothSlots = new DualSenseBluetoothSpeakerPassthrough[ControllerCount];
        private readonly int[] bluetoothStartGeneration = new int[ControllerCount];
        private readonly bool[] bluetoothStartPending = new bool[ControllerCount];
        private readonly bool[] startFailed = new bool[ControllerCount];
        private readonly object[] bluetoothStartGates = Enumerable.Range(0,
            ControllerCount).Select(_ => new object()).ToArray();
        private IWaveIn capture;
        private WaveFormat captureFormat;
        private string captureEndpointId = string.Empty;
        private ControllerAudioEndpointKind captureEndpointKind;
        private int captureUsbipPort = -1;
        private bool disposed;

        public ControllerRuntimeLaneState GetStatus(int slot)
        {
            if (slot < 0 || slot >= slots.Length)
            {
                return ControllerRuntimeLaneState.Unavailable;
            }

            lock (syncRoot)
            {
                if (slots[slot] != null || bluetoothSlots[slot] != null)
                {
                    return ControllerRuntimeLaneState.Ready;
                }

                if (startFailed[slot])
                {
                    return ControllerRuntimeLaneState.Unavailable;
                }

                return bluetoothStartPending[slot]
                    ? ControllerRuntimeLaneState.Starting
                    : ControllerRuntimeLaneState.Unavailable;
            }
        }

        public void Start(int slot, DualSenseDevice dualSenseDevice, byte speakerVolume,
            DualSenseSpeakerCompression speakerCompression, byte speakerBassBoost,
            string requestedCaptureEndpointId, string requestedSpeakerEndpointId,
            OutContType emulatedControllerType,
            ViiperOutDevice directSpeakerSource = null,
            Func<ViiperOutDevice> directSpeakerSourceResolver = null)
        {
            if (slot < 0 || slot >= slots.Length)
            {
                return;
            }

            lock (syncRoot)
            {
                startFailed[slot] = false;
            }

            if (dualSenseDevice?.ConnectionType == ConnectionType.BT)
            {
                StartBluetooth(slot, dualSenseDevice, speakerVolume, speakerCompression,
                    speakerBassBoost, requestedCaptureEndpointId,
                    emulatedControllerType, directSpeakerSource,
                    directSpeakerSourceResolver);
                return;
            }

            requestedCaptureEndpointId ??= string.Empty;
            requestedSpeakerEndpointId ??= string.Empty;
            lock (bluetoothStartGates[slot])
            {
                DualSenseBluetoothSpeakerPassthrough bluetoothPlayback;
                lock (syncRoot)
                {
                    if (disposed)
                    {
                        return;
                    }

                    bluetoothPlayback = bluetoothSlots[slot];
                    bluetoothSlots[slot] = null;
                    bluetoothStartPending[slot] = false;
                    bluetoothStartGeneration[slot]++;
                }

                // The per-slot gate is the ownership barrier between a retiring
                // Bluetooth transport and any replacement start. The global
                // manager lock is deliberately not held across its worker joins.
                bluetoothPlayback?.Dispose();

                SlotPlayback playbackToDispose = null;
                SlotPlayback failedPlayback = null;
                bool endpointMissing = false;

                try
                {
                    lock (syncRoot)
                    {
                        if (disposed)
                        {
                            return;
                        }

                        MMDevice endpoint = FindControllerEndpoint(slot,
                            requestedSpeakerEndpointId, dualSenseDevice);
                        if (endpoint == null)
                        {
                            AppLogger.LogToGui(
                                "DualSense audio passthrough could not find a controller speaker endpoint.",
                                true);
                            playbackToDispose = slots[slot];
                            slots[slot] = null;
                            if (!slots.Any(item => item != null))
                            {
                                StopCapture();
                            }

                            endpointMissing = true;
                            startFailed[slot] = true;
                        }
                        else if (slots[slot] != null && string.Equals(
                            slots[slot].EndpointId, endpoint.ID,
                            StringComparison.Ordinal))
                        {
                            slots[slot].SpeakerVolume = speakerVolume;
                            EnsureCaptureStarted(requestedCaptureEndpointId,
                                endpoint.ID,
                                GetEndpointKind(emulatedControllerType),
                                directSpeakerSource?.DirectSpeakerUsbipPort ?? -1);
                            return;
                        }
                        else
                        {
                            playbackToDispose = slots[slot];
                            slots[slot] = null;
                            if (!slots.Any(item => item != null))
                            {
                                StopCapture();
                            }

                            WaveFormat outputFormat = endpoint.AudioClient.MixFormat;
                            var provider = new BufferedWaveProvider(outputFormat)
                            {
                                BufferDuration = TimeSpan.FromMilliseconds(250),
                                DiscardOnBufferOverflow = true,
                            };

                            var output = new WasapiOut(endpoint,
                                AudioClientShareMode.Shared, true, 40);
                            output.Init(provider);
                            output.Play();

                            slots[slot] = new SlotPlayback(endpoint.ID, output,
                                provider, outputFormat, speakerVolume);
                            EnsureCaptureStarted(requestedCaptureEndpointId,
                                endpoint.ID,
                                GetEndpointKind(emulatedControllerType),
                                directSpeakerSource?.DirectSpeakerUsbipPort ?? -1);
                            AppLogger.LogToGui(
                                $"DualSense audio passthrough started for controller {slot + 1}: {endpoint.FriendlyName}",
                                false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogToGui($"DualSense audio passthrough failed to start: {ex.Message}", true);
                    lock (syncRoot)
                    {
                        failedPlayback = slots[slot];
                        startFailed[slot] = true;
                        slots[slot] = null;
                        if (!slots.Any(item => item != null))
                        {
                            StopCapture();
                        }
                    }
                }

                playbackToDispose?.Dispose();
                if (!ReferenceEquals(failedPlayback, playbackToDispose))
                {
                    failedPlayback?.Dispose();
                }

                if (endpointMissing)
                {
                    return;
                }
            }
        }

        public void Stop(int slot)
        {
            if (slot < 0 || slot >= slots.Length)
            {
                return;
            }

            lock (bluetoothStartGates[slot])
            {
                SlotPlayback playback;
                DualSenseBluetoothSpeakerPassthrough bluetoothPlayback;
                lock (syncRoot)
                {
                    playback = slots[slot];
                    slots[slot] = null;

                    bluetoothPlayback = bluetoothSlots[slot];
                    bluetoothSlots[slot] = null;
                    bluetoothStartPending[slot] = false;
                    bluetoothStartGeneration[slot]++;
                    startFailed[slot] = true;

                    if (!slots.Any(item => item != null))
                    {
                        StopCapture();
                    }
                }

                playback?.Dispose();
                bluetoothPlayback?.Dispose();
            }
        }

        public void ResetForServiceStop()
        {
            var playbacks = new SlotPlayback[slots.Length];
            var bluetoothPlaybacks = new DualSenseBluetoothSpeakerPassthrough[slots.Length];

            lock (syncRoot)
            {
                disposed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    playbacks[i] = slots[i];
                    slots[i] = null;
                    bluetoothPlaybacks[i] = bluetoothSlots[i];
                    bluetoothSlots[i] = null;
                    bluetoothStartPending[i] = false;
                    startFailed[i] = false;
                    bluetoothStartGeneration[i]++;
                }

                StopCapture();
            }

            for (int i = 0; i < slots.Length; i++)
            {
                lock (bluetoothStartGates[i])
                {
                    playbacks[i]?.Dispose();
                    bluetoothPlaybacks[i]?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            var playbacks = new SlotPlayback[slots.Length];
            var bluetoothPlaybacks =
                new DualSenseBluetoothSpeakerPassthrough[bluetoothSlots.Length];
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                for (int i = 0; i < slots.Length; i++)
                {
                    playbacks[i] = slots[i];
                    slots[i] = null;
                    bluetoothPlaybacks[i] = bluetoothSlots[i];
                    bluetoothSlots[i] = null;
                    bluetoothStartPending[i] = false;
                    startFailed[i] = false;
                    bluetoothStartGeneration[i]++;
                }

                StopCapture();
            }

            for (int i = 0; i < slots.Length; i++)
            {
                lock (bluetoothStartGates[i])
                {
                    playbacks[i]?.Dispose();
                    bluetoothPlaybacks[i]?.Dispose();
                }
            }
        }

        private void StartBluetooth(int slot, DualSenseDevice device, byte speakerVolume,
            DualSenseSpeakerCompression speakerCompression, byte speakerBassBoost,
            string requestedCaptureEndpointId, OutContType emulatedControllerType,
            ViiperOutDevice directSpeakerSource,
            Func<ViiperOutDevice> directSpeakerSourceResolver)
        {
            requestedCaptureEndpointId ??= string.Empty;
            ControllerAudioEndpointKind endpointKind = GetEndpointKind(emulatedControllerType);
            ViiperOutDevice currentDirectSpeakerSource =
                ResolveDirectSpeakerSource(directSpeakerSourceResolver,
                    directSpeakerSource);
            DirectSpeakerRouteDecision initialRoute =
                EvaluateDirectSpeakerRoute(requestedCaptureEndpointId,
                    endpointKind, currentDirectSpeakerSource);
            if (initialRoute == DirectSpeakerRouteDecision.Loopback)
            {
                currentDirectSpeakerSource = null;
            }

            int generation;
            lock (bluetoothStartGates[slot])
            {
                SlotPlayback usbPlayback;
                lock (syncRoot)
                {
                    if (disposed)
                    {
                        return;
                    }

                    if (bluetoothSlots[slot]?.Matches(device, speakerVolume,
                        speakerCompression, speakerBassBoost,
                        requestedCaptureEndpointId, endpointKind,
                        currentDirectSpeakerSource) == true)
                    {
                        return;
                    }

                    usbPlayback = slots[slot];
                    slots[slot] = null;
                    if (!slots.Any(item => item != null))
                    {
                        StopCapture();
                    }

                    // Keep the current Bluetooth playback alive until its
                    // replacement has activated a newer speaker-session token
                    // and published successfully. Retiring it first let its
                    // deferred End/Clear path own the unified transport while
                    // the replacement waited behind the same lock, producing
                    // multi-second silence after app-source changes.
                    generation = ++bluetoothStartGeneration[slot];
                    bluetoothStartPending[slot] = true;
                }

                usbPlayback?.Dispose();
            }

            _ = Task.Run(() => StartBluetoothWithRetry(slot, device, speakerVolume,
                speakerCompression, speakerBassBoost, requestedCaptureEndpointId,
                endpointKind, directSpeakerSource,
                directSpeakerSourceResolver, generation));
        }

        private void StartBluetoothWithRetry(int slot, DualSenseDevice device, byte speakerVolume,
            DualSenseSpeakerCompression speakerCompression, byte speakerBassBoost,
            string requestedCaptureEndpointId, ControllerAudioEndpointKind endpointKind,
            ViiperOutDevice directSpeakerSource,
            Func<ViiperOutDevice> directSpeakerSourceResolver,
            int generation)
        {
            Exception lastError = null;
            bool prolongedWaitLogged = false;
            for (int attempt = 0; ; attempt++)
            {
                if (TryStartBluetoothOnce(slot, device, speakerVolume,
                    speakerCompression, speakerBassBoost,
                    requestedCaptureEndpointId, endpointKind,
                    directSpeakerSource, directSpeakerSourceResolver,
                    generation, out lastError))
                {
                    return;
                }

                lock (syncRoot)
                {
                    if (disposed || generation != bluetoothStartGeneration[slot])
                    {
                        return;
                    }
                }

                // Endpoint enumeration is not an ownership operation. Keep the
                // per-slot gate free while waiting so disconnect/profile-change
                // teardown is immediate and can cancel this generation.
                if (!prolongedWaitLogged &&
                    attempt + 1 >= BluetoothStartRetryAttempts)
                {
                    prolongedWaitLogged = true;
                    AppLogger.LogToGui(
                        $"DualSense Bluetooth speaker is still waiting for its current VIIPER audio stream and will keep recovering in the background: {lastError?.Message}",
                        true);
                }

                Thread.Sleep(prolongedWaitLogged ? 1000 :
                    BluetoothStartRetryDelayMilliseconds);
            }
        }

        private bool TryStartBluetoothOnce(int slot,
            DualSenseDevice device, byte speakerVolume,
            DualSenseSpeakerCompression speakerCompression,
            byte speakerBassBoost, string requestedCaptureEndpointId,
            ControllerAudioEndpointKind endpointKind,
            ViiperOutDevice directSpeakerSource,
            Func<ViiperOutDevice> directSpeakerSourceResolver,
            int generation,
            out Exception lastError)
        {
            lastError = null;
            DualSenseBluetoothSpeakerPassthrough playbackToDispose = null;
            bool started = false;
            // At most one actual transport construction may own this physical
            // slot. The short gate spans start/publish, never retry sleeps.
            lock (bluetoothStartGates[slot])
            {
                lock (syncRoot)
                {
                    if (disposed ||
                        generation != bluetoothStartGeneration[slot])
                    {
                        return false;
                    }
                }

                ViiperOutDevice currentDirectSpeakerSource =
                    ResolveDirectSpeakerSource(directSpeakerSourceResolver,
                        directSpeakerSource);
                bool directRouteExpected = directSpeakerSourceResolver != null &&
                    !ProcessLoopbackWaveCapture.IsProcessEndpointId(
                        requestedCaptureEndpointId) &&
                    !string.Equals(requestedCaptureEndpointId,
                        DefaultSystemAudioEndpointId,
                        StringComparison.Ordinal);
                if (directRouteExpected && currentDirectSpeakerSource == null)
                {
                    lastError = new InvalidOperationException(
                        "The current VIIPER controller audio stream is being recreated.");
                    return false;
                }

                DirectSpeakerRouteDecision route =
                    EvaluateDirectSpeakerRoute(requestedCaptureEndpointId,
                        endpointKind, currentDirectSpeakerSource);
                if (route == DirectSpeakerRouteDecision.Pending)
                {
                    lastError = new InvalidOperationException(
                        "The selected VIIPER controller audio endpoint is still enumerating or its direct stream is recovering.");
                    return false;
                }

                ViiperOutDevice activeDirectSpeakerSource =
                    route == DirectSpeakerRouteDecision.Direct ?
                        currentDirectSpeakerSource : null;
                var bluetoothPlayback = new DualSenseBluetoothSpeakerPassthrough(device,
                    speakerVolume, speakerCompression, speakerBassBoost,
                    requestedCaptureEndpointId, endpointKind,
                    activeDirectSpeakerSource);
                try
                {
                    bluetoothPlayback.Start();
                    bool superseded;
                    lock (syncRoot)
                    {
                        superseded = disposed ||
                            generation != bluetoothStartGeneration[slot];
                        if (!superseded)
                        {
                            playbackToDispose = bluetoothSlots[slot];
                            bluetoothSlots[slot] = bluetoothPlayback;
                            bluetoothStartPending[slot] = false;
                            startFailed[slot] = false;
                            started = true;
                        }
                    }

                    if (superseded)
                    {
                        playbackToDispose = bluetoothPlayback;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    playbackToDispose = bluetoothPlayback;
                }
            }

            // Dispose only after the replacement has atomically become the
            // active session. The old worker can no longer clear or reset the
            // new session, and teardown never holds the per-slot start gate.
            playbackToDispose?.Dispose();
            return started;
        }

        private static ViiperOutDevice ResolveDirectSpeakerSource(
            Func<ViiperOutDevice> resolver, ViiperOutDevice fallback)
        {
            if (resolver == null)
            {
                return fallback;
            }

            try
            {
                return resolver();
            }
            catch
            {
                return null;
            }
        }

        private void StopBluetooth(int slot)
        {
            lock (bluetoothStartGates[slot])
            {
                DualSenseBluetoothSpeakerPassthrough bluetoothPlayback;
                lock (syncRoot)
                {
                    bluetoothPlayback = bluetoothSlots[slot];
                    bluetoothSlots[slot] = null;
                    bluetoothStartPending[slot] = false;
                    bluetoothStartGeneration[slot]++;
                    startFailed[slot] = true;
                }

                bluetoothPlayback?.Dispose();
            }
        }

        private void EnsureCaptureStarted(string requestedCaptureEndpointId,
            string speakerEndpointId, ControllerAudioEndpointKind endpointKind,
            int preferredUsbipPort = -1)
        {
            requestedCaptureEndpointId ??= string.Empty;
            speakerEndpointId ??= string.Empty;

            if (capture != null && captureEndpointKind == endpointKind && captureUsbipPort == preferredUsbipPort &&
                string.Equals(captureEndpointId, requestedCaptureEndpointId, StringComparison.Ordinal))
            {
                return;
            }

            StopCapture();

            if (ProcessLoopbackWaveCapture.TryParseAutomaticEndpointId(
                    requestedCaptureEndpointId, out int automaticSlot))
            {
                capture = ProcessLoopbackWaveCapture.CreateAutomatic(
                    automaticSlot);
                captureEndpointId = requestedCaptureEndpointId;
                captureEndpointKind = endpointKind;
                captureUsbipPort = preferredUsbipPort;
                captureFormat = capture.WaveFormat;
                capture.DataAvailable += Capture_DataAvailable;
                capture.RecordingStopped += Capture_RecordingStopped;
                capture.StartRecording();
                AppLogger.LogToGui(
                    "DualSense audio passthrough capture source: automatic game detection",
                    false);
                return;
            }

            if (ProcessLoopbackWaveCapture.TryParseEndpointId(
                    requestedCaptureEndpointId, out int processId))
            {
                capture = new ProcessLoopbackWaveCapture(processId);
                captureEndpointId = requestedCaptureEndpointId;
                captureEndpointKind = endpointKind;
                captureUsbipPort = preferredUsbipPort;
                captureFormat = capture.WaveFormat;
                capture.DataAvailable += Capture_DataAvailable;
                capture.RecordingStopped += Capture_RecordingStopped;
                capture.StartRecording();
                AppLogger.LogToGui(
                    $"DualSense audio passthrough capture source: selected app (process {processId})",
                    false);
                return;
            }

            if (ProcessLoopbackWaveCapture.IsProcessEndpointId(
                    requestedCaptureEndpointId))
            {
                throw new InvalidOperationException(
                    "The selected app is not running, so its audio cannot be streamed to the controller.");
            }

            MMDevice sourceEndpoint = FindCaptureEndpoint(requestedCaptureEndpointId,
                speakerEndpointId, endpointKind, preferredUsbipPort);
            bool expectsControllerEndpoint =
                string.Equals(requestedCaptureEndpointId,
                    AutoDetectGameAudioEndpointId, StringComparison.Ordinal) ||
                (string.IsNullOrEmpty(requestedCaptureEndpointId) &&
                    endpointKind != ControllerAudioEndpointKind.Any);
            if (sourceEndpoint == null && expectsControllerEndpoint)
            {
                AppLogger.LogToGui(
                    "Emulated controller audio endpoint is not available yet. Waiting for the virtual controller to enumerate.",
                    true);
                return;
            }

            if (sourceEndpoint == null && string.IsNullOrEmpty(requestedCaptureEndpointId) && DefaultEndpointMatches(speakerEndpointId))
            {
                AppLogger.LogToGui("DualSense audio passthrough cannot capture from the same endpoint it is playing to.", true);
                return;
            }

            // Resolve the default endpoint too: it can be a Sonar software
            // route and needs the same capture policy as an explicit choice.
            sourceEndpoint ??= WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
            capture = ControllerEndpointLoopbackCaptureFactory.Create(
                sourceEndpoint, endpoint => new WasapiLoopbackCapture(endpoint),
                "DualSense USB speaker");
            captureEndpointId = requestedCaptureEndpointId;
            captureEndpointKind = endpointKind;
            captureUsbipPort = preferredUsbipPort;
            captureFormat = capture.WaveFormat;
            capture.DataAvailable += Capture_DataAvailable;
            capture.RecordingStopped += Capture_RecordingStopped;
            capture.StartRecording();

            string sourceName = sourceEndpoint?.FriendlyName ?? "Default";
            AppLogger.LogToGui($"DualSense audio passthrough capture source: {sourceName}", false);
        }

        private void StopCapture()
        {
            IWaveIn oldCapture = capture;
            capture = null;
            captureFormat = null;
            captureEndpointId = string.Empty;
            captureEndpointKind = ControllerAudioEndpointKind.Any;
            captureUsbipPort = -1;
            foreach (SlotPlayback slot in slots)
            {
                slot?.ResetCaptureSource();
            }

            if (oldCapture == null)
            {
                return;
            }

            oldCapture.DataAvailable -= Capture_DataAvailable;
            oldCapture.RecordingStopped -= Capture_RecordingStopped;

            // WASAPI disposal joins its capture thread. Retire outside the manager
            // lock so a callback already waiting for that lock can observe its
            // revoked identity and return instead of deadlocking the join.
            _ = Task.Run(() =>
            {
                try
                {
                    oldCapture.StopRecording();
                }
                catch (Exception ex)
                {
                    try
                    {
                        AppLogger.LogToGui($"DualSense audio capture cleanup could not stop its retired source: {ex.Message}", true);
                    }
                    catch { } // A retiring UI logger must not prevent resource disposal.
                }
                try
                {
                    oldCapture.Dispose();
                }
                catch (Exception ex)
                {
                    try
                    {
                        AppLogger.LogToGui($"DualSense audio capture cleanup could not dispose its retired source: {ex.Message}", true);
                    }
                    catch { } // Do not leave an unobserved retirement task on UI shutdown.
                }
            });
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                AppLogger.LogToGui($"DualSense audio passthrough capture stopped: {e.Exception.Message}", true);
            }
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            lock (syncRoot)
            {
                if (!ReferenceEquals(sender, capture) ||
                    captureFormat == null || captureFormat.Channels < 1)
                {
                    return;
                }

                int frames = e.BytesRecorded / captureFormat.BlockAlign;
                if (frames <= 0)
                {
                    return;
                }

                foreach (SlotPlayback slot in slots)
                {
                    slot?.WriteFromCapture(e.Buffer, frames, captureFormat);
                }
            }
        }

        private MMDevice FindControllerEndpoint(int slot, string requestedSpeakerEndpointId,
            DualSenseDevice physicalDevice)
        {
            HashSet<string> usedIds = slots
                .Where((item, index) => item != null && index != slot)
                .Select(item => item.EndpointId)
                .ToHashSet(StringComparer.Ordinal);

            using var enumerator = new MMDeviceEnumerator();
            if (!string.IsNullOrEmpty(requestedSpeakerEndpointId))
            {
                try
                {
                    MMDevice requested = enumerator.GetDevice(requestedSpeakerEndpointId);
                    if (requested.State == DeviceState.Active && !usedIds.Contains(requested.ID))
                    {
                        return requested;
                    }
                }
                catch
                {
                    AppLogger.LogToGui("Selected DualSense speaker endpoint was not found. Falling back to auto-detect.", true);
                }
            }

            // The emulated controller also exposes a DualSense audio endpoint.
            // Match the physical controller's container instead of enumeration order.
            MMDevice autoEndpoint = physicalDevice?.HidDevice == null ? null :
                DualSenseUsbHapticsPassthrough.FindPhysicalEndpoint(enumerator,
                    physicalDevice, string.Empty);
            if (autoEndpoint != null && usedIds.Contains(autoEndpoint.ID))
            {
                autoEndpoint.Dispose();
                autoEndpoint = null;
            }

            if (autoEndpoint == null)
            {
                string endpointNames = string.Join(", ", enumerator
                    .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Where(device => !usedIds.Contains(device.ID))
                    .Select(endpoint => endpoint.FriendlyName));

                AppLogger.LogToGui(
                    string.IsNullOrEmpty(endpointNames) ?
                        "No active Windows playback endpoints were found for DualSense speaker passthrough." :
                        $"DualSense speaker auto-detect failed. Active playback endpoints: {endpointNames}",
                    true);
            }

            return autoEndpoint;
        }

        private static MMDevice FindCaptureEndpoint(string endpointId, string speakerEndpointId,
            ControllerAudioEndpointKind endpointKind, int preferredUsbipPort = -1)
        {
            bool useSystemDefault = string.Equals(endpointId,
                DefaultSystemAudioEndpointId, StringComparison.Ordinal) ||
                (string.IsNullOrEmpty(endpointId) &&
                endpointKind == ControllerAudioEndpointKind.Any);
            if (useSystemDefault)
            {
                return null;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                bool autoDetect = string.IsNullOrEmpty(endpointId) ||
                    string.Equals(endpointId, AutoDetectGameAudioEndpointId,
                        StringComparison.Ordinal);
                MMDevice endpoint = autoDetect ?
                    FindActiveGameAudioEndpoint(enumerator, null, endpointKind,
                        preferredUsbipPort, speakerEndpointId) :
                    enumerator.GetDevice(endpointId);
                if (endpoint?.State != DeviceState.Active)
                {
                    endpoint?.Dispose();
                    return null;
                }

                if (autoDetect && preferredUsbipPort >= 0 &&
                    !EndpointMatchesUsbipPort(endpoint, preferredUsbipPort))
                {
                    endpoint.Dispose();
                    return null;
                }

                if (string.Equals(endpoint.ID, speakerEndpointId, StringComparison.Ordinal))
                {
                    AppLogger.LogToGui("DualSense audio passthrough capture source cannot be the same as the speaker endpoint. Falling back to default audio endpoint.", true);
                    return null;
                }

                return endpoint;
            }
            catch
            {
                AppLogger.LogToGui("Controller audio passthrough capture source was not found. Falling back to default audio endpoint.", true);
                return null;
            }
        }

        private static bool DefaultEndpointMatches(string endpointId)
        {
            if (string.IsNullOrEmpty(endpointId))
            {
                return false;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return string.Equals(endpoint?.ID, endpointId, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDualSenseEndpoint(MMDevice device)
        {
            ControllerAudioEndpointKind kind = ClassifyEndpoint(device);
            if (kind == ControllerAudioEndpointKind.DualSense)
            {
                return true;
            }

            // Older endpoint property stores can omit their USB instance ID.
            // Keep the historic generic fallback, but never mistake a positively
            // identified DS4 endpoint for a physical DualSense speaker.
            return kind == ControllerAudioEndpointKind.Any &&
                GetEndpointIdentity(device).IndexOf("Wireless Controller",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsControllerAudioEndpoint(MMDevice device)
        {
            string identity = GetEndpointQuickIdentity(device);
            if (LooksLikeControllerAudioIdentity(identity))
            {
                return true;
            }

            // Avoid opening slow driver property stores for ordinary desktop
            // speakers, HDMI outputs, and virtual mixers. Only ambiguous devices
            // whose visible identity suggests a controller need the full probe.
            if (!LooksLikeAmbiguousControllerIdentity(identity))
            {
                return false;
            }

            identity = GetEndpointIdentity(device);
            return LooksLikeControllerAudioIdentity(identity);
        }

        private static string GetEndpointQuickIdentity(MMDevice endpoint)
        {
            if (endpoint == null)
            {
                return string.Empty;
            }

            try
            {
                return string.Join(" ", endpoint.ID ?? string.Empty,
                    endpoint.FriendlyName ?? string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool LooksLikeControllerAudioIdentity(string identity)
        {
            return ClassifyEndpointIdentity(identity) != ControllerAudioEndpointKind.Any ||
                identity.IndexOf("Wireless Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("VIIPER", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeAmbiguousControllerIdentity(string identity)
        {
            return identity.IndexOf("controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("sony", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("playstation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("054c", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static MMDevice FindActiveGameAudioEndpoint(MMDeviceEnumerator enumerator,
            string previousEndpointId = null,
            ControllerAudioEndpointKind preferredKind = ControllerAudioEndpointKind.Any,
            int preferredUsbipPort = -1, string excludedEndpointId = null)
        {
            IEnumerable<MMDevice> endpoints = enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Where(endpoint => !string.Equals(endpoint.ID, excludedEndpointId, StringComparison.Ordinal))
                .Where(IsControllerAudioEndpoint);

            if (preferredKind != ControllerAudioEndpointKind.Any)
            {
                endpoints = endpoints.Where(endpoint =>
                {
                    ControllerAudioEndpointKind kind = ClassifyEndpoint(endpoint);
                    return kind == preferredKind || kind == ControllerAudioEndpointKind.Any;
                });
            }

            return endpoints
                .OrderByDescending(endpoint =>
                    EndpointMatchesUsbipPort(endpoint, preferredUsbipPort))
                .ThenByDescending(endpoint => EndpointScore(endpoint,
                    preferredKind, previousEndpointId))
                .FirstOrDefault();
        }

        private static bool EndpointMatchesUsbipPort(MMDevice endpoint,
            int preferredUsbipPort)
        {
            if (preferredUsbipPort < 0 || endpoint == null)
            {
                return false;
            }

            return TryGetEndpointUsbipPort(endpoint, out int endpointPort) &&
                endpointPort == preferredUsbipPort;
        }

        internal static bool TryGetEndpointUsbipPort(MMDevice endpoint,
            out int endpointPort)
        {
            endpointPort = -1;
            if (endpoint == null)
            {
                return false;
            }

            string interfacePath = GetEndpointProperty(endpoint,
                PropertyKeys.PKEY_Device_InterfaceKey);
            int pathStart = interfacePath.IndexOf(@"\\?\",
                StringComparison.Ordinal);
            if (pathStart > 0)
            {
                interfacePath = interfacePath.Substring(pathStart);
            }

            return !string.IsNullOrEmpty(interfacePath) &&
                Global.TryResolveUsbIpWin2Device(interfacePath,
                    out bool usbIpAncestor, out endpointPort) &&
                usbIpAncestor && endpointPort >= 0;
        }

        internal static ControllerAudioEndpointKind GetEndpointKind(OutContType outputType)
        {
            outputType = outputType.Normalize();
            return outputType switch
            {
                OutContType.ViiperDS4 => ControllerAudioEndpointKind.DualShock4,
                OutContType.ViiperDualSense or OutContType.ViiperDualSenseEdge =>
                    ControllerAudioEndpointKind.DualSense,
                _ => ControllerAudioEndpointKind.Any,
            };
        }

        internal static bool IsDirectSpeakerRequest(string endpointId,
            bool explicitEndpointOwnedByDirectSource)
        {
            endpointId ??= string.Empty;
            if (ProcessLoopbackWaveCapture.IsProcessEndpointId(endpointId))
            {
                return false;
            }
            if (string.IsNullOrEmpty(endpointId) ||
                string.Equals(endpointId, AutoDetectGameAudioEndpointId,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(endpointId, DefaultSystemAudioEndpointId,
                StringComparison.Ordinal))
            {
                return false;
            }

            return explicitEndpointOwnedByDirectSource;
        }

        internal static DirectSpeakerRouteDecision DecideDirectSpeakerRoute(
            string endpointId, bool directSourceCapable,
            bool directStreamActive,
            DirectSpeakerEndpointOwnership explicitEndpointOwnership)
        {
            endpointId ??= string.Empty;
            if (ProcessLoopbackWaveCapture.IsProcessEndpointId(endpointId))
            {
                return DirectSpeakerRouteDecision.Loopback;
            }
            if (string.Equals(endpointId, DefaultSystemAudioEndpointId,
                StringComparison.Ordinal))
            {
                return DirectSpeakerRouteDecision.Loopback;
            }

            if (!directSourceCapable)
            {
                return DirectSpeakerRouteDecision.Loopback;
            }

            bool automatic = string.IsNullOrEmpty(endpointId) ||
                string.Equals(endpointId, AutoDetectGameAudioEndpointId,
                    StringComparison.Ordinal);
            if (automatic)
            {
                return directStreamActive ? DirectSpeakerRouteDecision.Direct :
                    DirectSpeakerRouteDecision.Pending;
            }

            return explicitEndpointOwnership switch
            {
                DirectSpeakerEndpointOwnership.Owned when directStreamActive =>
                    DirectSpeakerRouteDecision.Direct,
                DirectSpeakerEndpointOwnership.Owned =>
                    DirectSpeakerRouteDecision.Pending,
                // VIIPER controller audio endpoints are recreated with a new
                // MMDevice GUID. An old concrete controller GUID must not
                // leave the physical speaker waiting forever. Once the
                // controller-bound direct stream is live, it is the safe
                // replacement for a saved endpoint that no longer exists.
                DirectSpeakerEndpointOwnership.Missing when directStreamActive =>
                    DirectSpeakerRouteDecision.Direct,
                DirectSpeakerEndpointOwnership.Missing =>
                    DirectSpeakerRouteDecision.Pending,
                DirectSpeakerEndpointOwnership.Unowned =>
                    DirectSpeakerRouteDecision.Loopback,
                _ => DirectSpeakerRouteDecision.Pending,
            };
        }

        internal static DirectSpeakerRouteDecision EvaluateDirectSpeakerRoute(
            string endpointId,
            ControllerAudioEndpointKind endpointKind,
            ViiperOutDevice directSpeakerSource)
        {
            bool capable = directSpeakerSource?.CanProvideDirectSpeakerPcm == true;
            bool active = directSpeakerSource?.SupportsDirectSpeakerPcm == true;
            DirectSpeakerEndpointOwnership ownership =
                DirectSpeakerEndpointOwnership.Unresolved;
            bool explicitEndpoint = !string.IsNullOrEmpty(endpointId) &&
                !string.Equals(endpointId, AutoDetectGameAudioEndpointId,
                    StringComparison.Ordinal) &&
                !string.Equals(endpointId, DefaultSystemAudioEndpointId,
                    StringComparison.Ordinal);
            if (capable && explicitEndpoint)
            {
                ownership = ResolveExplicitEndpointOwnership(endpointId,
                    endpointKind, directSpeakerSource);
            }

            return DecideDirectSpeakerRoute(endpointId, capable, active,
                ownership);
        }

        private static DirectSpeakerEndpointOwnership
            ResolveExplicitEndpointOwnership(
            string endpointId, ControllerAudioEndpointKind endpointKind,
            ViiperOutDevice directSpeakerSource)
        {
            if (string.IsNullOrEmpty(endpointId) ||
                directSpeakerSource == null)
            {
                return DirectSpeakerEndpointOwnership.Unresolved;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                List<MMDevice> activeEndpoints = enumerator
                    .EnumerateAudioEndPoints(DataFlow.Render,
                        DeviceState.Active).ToList();
                try
                {
                    MMDevice exactEndpoint = activeEndpoints.FirstOrDefault(
                        endpoint => string.Equals(endpoint.ID, endpointId,
                            StringComparison.Ordinal));
                    if (exactEndpoint != null)
                    {
                        if (IsControllerEndpointSelection(
                                ClassifyEndpoint(exactEndpoint), endpointKind))
                        {
                            return DirectSpeakerEndpointOwnership.Owned;
                        }

                        return ResolveEndpointOwnership(exactEndpoint,
                            endpointKind, directSpeakerSource);
                    }

                    DirectSpeakerEndpointOwnership replacementResult =
                        DirectSpeakerEndpointOwnership.Unresolved;
                    foreach (MMDevice candidate in activeEndpoints.Where(
                        endpoint => EndpointReplaces(endpoint, endpointId)))
                    {
                        if (IsControllerEndpointSelection(
                                ClassifyEndpoint(candidate), endpointKind))
                        {
                            return DirectSpeakerEndpointOwnership.Owned;
                        }

                        DirectSpeakerEndpointOwnership candidateResult =
                            ResolveEndpointOwnership(candidate, endpointKind,
                                directSpeakerSource);
                        if (candidateResult ==
                            DirectSpeakerEndpointOwnership.Owned)
                        {
                            return candidateResult;
                        }

                        if (candidateResult ==
                            DirectSpeakerEndpointOwnership.Unowned)
                        {
                            replacementResult = candidateResult;
                        }
                    }

                    if (replacementResult !=
                        DirectSpeakerEndpointOwnership.Unresolved)
                    {
                        return replacementResult;
                    }
                }
                finally
                {
                    foreach (MMDevice endpoint in activeEndpoints)
                    {
                        endpoint.Dispose();
                    }
                }

                bool savedEndpointMissing = false;
                try
                {
                    using MMDevice savedEndpoint =
                        enumerator.GetDevice(endpointId);
                    if (savedEndpoint != null &&
                        IsControllerEndpointSelection(
                            ClassifyEndpoint(savedEndpoint), endpointKind))
                    {
                        return DirectSpeakerEndpointOwnership.Owned;
                    }

                    if (savedEndpoint?.State == DeviceState.Active)
                    {
                        return ResolveEndpointOwnership(savedEndpoint,
                            endpointKind, directSpeakerSource);
                    }

                }
                catch
                {
                    savedEndpointMissing = true;
                }

                return savedEndpointMissing
                    ? DirectSpeakerEndpointOwnership.Missing
                    : DirectSpeakerEndpointOwnership.Unresolved;
            }
            catch
            {
                return DirectSpeakerEndpointOwnership.Unresolved;
            }
        }

        internal static bool IsControllerEndpointSelection(
            ControllerAudioEndpointKind savedEndpointKind,
            ControllerAudioEndpointKind currentOutputKind)
        {
            bool savedIsSpecificController =
                savedEndpointKind == ControllerAudioEndpointKind.DualShock4 ||
                savedEndpointKind == ControllerAudioEndpointKind.DualSense;
            bool currentIsSpecificController =
                currentOutputKind == ControllerAudioEndpointKind.DualShock4 ||
                currentOutputKind == ControllerAudioEndpointKind.DualSense;
            // Concrete Sony endpoint GUIDs are recreated when VIIPER restarts
            // or changes persona. Preserve the user's controller-audio intent
            // across both same-persona recreation and DS4/DualSense changes.
            // Non-controller endpoints keep literal loopback semantics.
            return savedIsSpecificController && currentIsSpecificController;
        }

        private static DirectSpeakerEndpointOwnership ResolveEndpointOwnership(
            MMDevice endpoint, ControllerAudioEndpointKind endpointKind,
            ViiperOutDevice directSpeakerSource)
        {
            bool identityMatches = EndpointKindMatches(endpoint, endpointKind);
            string interfacePath = GetEndpointProperty(endpoint,
                PropertyKeys.PKEY_Device_InterfaceKey);
            int pathStart = interfacePath.IndexOf(@"\\?\",
                StringComparison.Ordinal);
            if (pathStart > 0)
            {
                interfacePath = interfacePath.Substring(pathStart);
            }

            bool interfacePathAvailable =
                !string.IsNullOrEmpty(interfacePath);
            int endpointPort = -1;
            bool usbIpAncestor = false;
            bool usbIpQueryResolved = interfacePathAvailable &&
                Global.TryResolveUsbIpWin2Device(interfacePath,
                    out usbIpAncestor, out endpointPort);

            return ClassifyDirectSpeakerEndpointOwnership(
                endpoint?.State == DeviceState.Active, identityMatches,
                interfacePathAvailable, usbIpQueryResolved, usbIpAncestor,
                endpointPort, directSpeakerSource?.DirectSpeakerUsbipPort ?? -1);
        }

        internal static DirectSpeakerEndpointOwnership
            ClassifyDirectSpeakerEndpointOwnership(bool endpointActive,
            bool controllerIdentityMatches, bool interfacePathAvailable,
            bool usbIpQueryResolved, bool usbIpAncestor, int endpointPort,
            int sourcePort)
        {
            if (!endpointActive)
            {
                return DirectSpeakerEndpointOwnership.Unresolved;
            }

            if (!controllerIdentityMatches)
            {
                return DirectSpeakerEndpointOwnership.Unowned;
            }

            if (!interfacePathAvailable)
            {
                return DirectSpeakerEndpointOwnership.Unresolved;
            }

            if (!usbIpQueryResolved)
            {
                return DirectSpeakerEndpointOwnership.Unresolved;
            }

            if (!usbIpAncestor)
            {
                return DirectSpeakerEndpointOwnership.Unowned;
            }

            if (endpointPort < 0 || sourcePort < 0)
            {
                return DirectSpeakerEndpointOwnership.Unresolved;
            }

            return endpointPort == sourcePort ?
                DirectSpeakerEndpointOwnership.Owned :
                DirectSpeakerEndpointOwnership.Unowned;
        }

        private static bool EndpointKindMatches(MMDevice endpoint,
            ControllerAudioEndpointKind expectedKind)
        {
            if (!IsControllerAudioEndpoint(endpoint))
            {
                return false;
            }

            ControllerAudioEndpointKind actualKind = ClassifyEndpoint(endpoint);
            return expectedKind == ControllerAudioEndpointKind.Any ||
                actualKind == expectedKind ||
                actualKind == ControllerAudioEndpointKind.Any;
        }

        internal static ControllerAudioEndpointKind ClassifyEndpointIdentity(string identity)
        {
            identity ??= string.Empty;
            string normalized = identity.Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty);

            if (ContainsSonyUsbIdentity(normalized, "05C4") ||
                ContainsSonyUsbIdentity(normalized, "09CC") ||
                identity.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("DS4", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ControllerAudioEndpointKind.DualShock4;
            }

            if (ContainsSonyUsbIdentity(normalized, "0CE6") ||
                ContainsSonyUsbIdentity(normalized, "0DF2") ||
                identity.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("PS5", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ControllerAudioEndpointKind.DualSense;
            }

            return ControllerAudioEndpointKind.Any;
        }

        private static ControllerAudioEndpointKind ClassifyEndpoint(MMDevice endpoint)
        {
            return ClassifyEndpointIdentity(GetEndpointIdentity(endpoint));
        }

        private static bool ContainsSonyUsbIdentity(string normalizedIdentity,
            string productId)
        {
            return normalizedIdentity.IndexOf("VID054C", StringComparison.OrdinalIgnoreCase) >= 0 &&
                normalizedIdentity.IndexOf("PID" + productId,
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetEndpointIdentity(MMDevice endpoint)
        {
            if (endpoint == null)
            {
                return string.Empty;
            }

            var values = new List<string>
            {
                endpoint.ID ?? string.Empty,
                endpoint.FriendlyName ?? string.Empty,
                endpoint.DeviceFriendlyName ?? string.Empty,
            };

            AddEndpointProperty(values, endpoint, PropertyKeys.PKEY_Device_InstanceId);
            AddEndpointProperty(values, endpoint, PropertyKeys.PKEY_Device_ControllerDeviceId);
            AddEndpointProperty(values, endpoint, PropertyKeys.PKEY_Device_InterfaceKey);
            return string.Join(" ", values);
        }

        private static string GetEndpointProperty(MMDevice endpoint,
            PropertyKey propertyKey)
        {
            try
            {
                return endpoint?.Properties[propertyKey]?.Value?.ToString() ??
                    string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AddEndpointProperty(List<string> values, MMDevice endpoint,
            PropertyKey propertyKey)
        {
            try
            {
                object value = endpoint.Properties[propertyKey]?.Value;
                if (value != null)
                {
                    values.Add(value.ToString());
                }
            }
            catch
            {
                // Endpoint property availability differs by Windows audio driver.
            }
        }

        private static int EndpointScore(MMDevice endpoint,
            ControllerAudioEndpointKind preferredKind, string previousEndpointId)
        {
            int score = 0;
            ControllerAudioEndpointKind actualKind = ClassifyEndpoint(endpoint);
            if (preferredKind != ControllerAudioEndpointKind.Any && actualKind == preferredKind)
            {
                score += 100;
            }
            else if (actualKind != ControllerAudioEndpointKind.Any)
            {
                score += 20;
            }

            string identity = GetEndpointIdentity(endpoint);
            if (identity.IndexOf("VIIPER", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }

            if (!string.IsNullOrEmpty(previousEndpointId) &&
                (string.Equals(endpoint.ID, previousEndpointId, StringComparison.Ordinal) ||
                EndpointReplaces(endpoint, previousEndpointId)))
            {
                score += 1000;
            }

            return score;
        }

        private static bool EndpointReplaces(MMDevice endpoint, string previousEndpointId)
        {
            string endpointId = endpoint?.ID ?? string.Empty;
            int keyStart = endpointId.LastIndexOf(".{", StringComparison.Ordinal);
            if (keyStart < 0)
            {
                return false;
            }

            try
            {
                string endpointKeyName = endpointId.Substring(keyStart + 1);
                using RegistryKey properties = Registry.LocalMachine.OpenSubKey(
                    RenderEndpointRegistryPath + endpointKeyName + @"\Properties");
                object history = properties?.GetValue(EndpointHistoryValueName);
                if (history is string[] endpointIds)
                {
                    return endpointIds.Any(id => string.Equals(id, previousEndpointId,
                        StringComparison.OrdinalIgnoreCase));
                }

                return history is string id && string.Equals(id, previousEndpointId,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private sealed class SlotPlayback : IDisposable
        {
            private const int ProcessingBlockFrames = 256;
            private readonly WasapiOut output;
            private readonly BufferedWaveProvider provider;
            private readonly WaveFormat outputFormat;
            private readonly byte[] outputBuffer;
            private NAudio.Dsp.WdlResampler resampler;
            private float[] convertedSamples;
            private int sourceSampleRate;
            private long sourceFramesReceived;
            private long outputFramesProduced;
            private WaveFormat lastCaptureFormat;
            private WaveFormat sampleCaptureFormat;

            public string EndpointId { get; }
            public byte SpeakerVolume { get; set; }

            public SlotPlayback(string endpointId, WasapiOut output, BufferedWaveProvider provider,
                WaveFormat outputFormat, byte speakerVolume)
            {
                EndpointId = endpointId;
                this.output = output;
                this.provider = provider;
                // Interpret the samples without changing the endpoint's negotiated format.
                this.outputFormat = outputFormat is WaveFormatExtensible extensible ?
                    extensible.AsStandardWaveFormat() : outputFormat;
                SpeakerVolume = speakerVolume;
                outputBuffer = new byte[4096 * outputFormat.BlockAlign];
            }

            public void WriteFromCapture(byte[] captureBuffer, int frames, WaveFormat captureFormat)
            {
                ArgumentNullException.ThrowIfNull(captureBuffer);
                ArgumentNullException.ThrowIfNull(captureFormat);
                if (frames < 0 || captureFormat.BlockAlign <= 0 ||
                    frames > captureBuffer.Length / captureFormat.BlockAlign)
                {
                    throw new ArgumentOutOfRangeException(nameof(frames));
                }
                if (frames == 0)
                {
                    return;
                }

                if (!ReferenceEquals(lastCaptureFormat, captureFormat))
                {
                    sampleCaptureFormat = captureFormat is WaveFormatExtensible extensible ?
                        extensible.AsStandardWaveFormat() : captureFormat;
                    lastCaptureFormat = captureFormat;
                }
                EnsureSourceRate(captureFormat.SampleRate);
                float volume = SpeakerVolume / 255.0f;
                int sourceFrame = 0;
                while (sourceFrame < frames)
                {
                    int blockFrames = Math.Min(frames - sourceFrame, ProcessingBlockFrames);
                    if (resampler == null)
                    {
                        // Native-rate audio needs neither filtering nor an extra waiting block.
                        for (int frame = 0; frame < blockFrames; frame++)
                        {
                            convertedSamples[frame] = ReadSpeakerSample(captureBuffer,
                                (sourceFrame + frame) * captureFormat.BlockAlign, sampleCaptureFormat);
                        }
                        WriteConvertedSamples(blockFrames, volume);
                    }
                    else
                    {
                        // Feed exactly the samples available now. Requesting a full block and
                        // supplying fewer samples would tell WDL to flush/pad the stream.
                        int requested = resampler.ResamplePrepare(blockFrames, 1,
                            out float[] input, out int inputOffset);
                        if (requested != blockFrames)
                        {
                            throw new InvalidOperationException("The speaker resampler rejected its input block.");
                        }
                        for (int frame = 0; frame < blockFrames; frame++)
                        {
                            input[inputOffset + frame] = ReadSpeakerSample(captureBuffer,
                                (sourceFrame + frame) * captureFormat.BlockAlign, sampleCaptureFormat);
                        }

                        sourceFramesReceived += blockFrames;
                        long dueFrames = sourceFramesReceived / sourceSampleRate * outputFormat.SampleRate +
                            sourceFramesReceived % sourceSampleRate * outputFormat.SampleRate / sourceSampleRate;
                        int outputCapacity = (int)Math.Min(convertedSamples.Length,
                            Math.Max(0, dueFrames - outputFramesProduced));
                        // Bound consumption to received source time, including for downsampling
                        // ratios above two. Keep WDL's fractional phase across callback boundaries.
                        // Its IIR output filter requires an output offset of zero (NAudio 2.2.1).
                        int produced = resampler.ResampleOut(convertedSamples, 0,
                            blockFrames, outputCapacity, 1);
                        outputFramesProduced += produced;
                        WriteConvertedSamples(produced, volume);
                    }
                    sourceFrame += blockFrames;
                }
            }

            private void EnsureSourceRate(int sampleRate)
            {
                if (sampleRate <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sampleRate));
                }
                if (sourceSampleRate == sampleRate)
                {
                    return;
                }
                sourceSampleRate = sampleRate;
                sourceFramesReceived = 0;
                outputFramesProduced = 0;
                if (sampleRate == outputFormat.SampleRate)
                {
                    resampler = null;
                    convertedSamples = new float[ProcessingBlockFrames];
                    return;
                }

                resampler = new NAudio.Dsp.WdlResampler();
                resampler.SetMode(true, 2, false);
                resampler.SetFeedMode(true);
                resampler.SetRates(sampleRate, outputFormat.SampleRate);
                convertedSamples = new float[checked((int)Math.Ceiling(
                    (ProcessingBlockFrames + 2.0) * outputFormat.SampleRate / sampleRate + 2.0))];
            }

            public void ResetCaptureSource()
            {
                resampler = null;
                convertedSamples = null;
                sourceSampleRate = 0;
                sourceFramesReceived = 0;
                outputFramesProduced = 0;
                lastCaptureFormat = null;
                sampleCaptureFormat = null;
                provider.ClearBuffer();
            }

            private static float ReadSpeakerSample(byte[] buffer, int offset, WaveFormat format)
            {
                float left = ReadSample(buffer, offset, format);
                float right = format.Channels > 1 ?
                    ReadSample(buffer, offset + BytesPerSample(format), format) : left;
                return (left + right) * 0.5f;
            }

            private void WriteConvertedSamples(int frames, float volume)
            {
                int frameOffset = 0;
                int speakerOffset = (outputFormat.Channels >= 4 ? 1 : 0) * BytesPerSample(outputFormat);
                while (frameOffset < frames)
                {
                    int blockFrames = Math.Min(frames - frameOffset,
                        outputBuffer.Length / outputFormat.BlockAlign);
                    int bytes = blockFrames * outputFormat.BlockAlign;
                    Array.Clear(outputBuffer, 0, bytes);
                    for (int frame = 0; frame < blockFrames; frame++)
                    {
                        WriteSample(outputBuffer, frame * outputFormat.BlockAlign + speakerOffset,
                            outputFormat, convertedSamples[frameOffset + frame] * volume);
                    }
                    provider.AddSamples(outputBuffer, 0, bytes);
                    frameOffset += blockFrames;
                }
            }

            private static int BytesPerSample(WaveFormat format)
            {
                return Math.Max(1, format.BitsPerSample / 8);
            }

            private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
            {
                if (offset < 0 || offset + BytesPerSample(format) > buffer.Length)
                {
                    return 0.0f;
                }

                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    if (format.BitsPerSample == 32)
                    {
                        return Math.Clamp(BitConverter.ToSingle(buffer, offset), -1.0f, 1.0f);
                    }

                    if (format.BitsPerSample == 64)
                    {
                        return Math.Clamp((float)BitConverter.ToDouble(buffer, offset), -1.0f, 1.0f);
                    }
                }

                if (format.Encoding != WaveFormatEncoding.Pcm)
                {
                    return 0.0f;
                }

                return format.BitsPerSample switch
                {
                    16 => BitConverter.ToInt16(buffer, offset) / 32768.0f,
                    24 => ReadInt24(buffer, offset) / 8388608.0f,
                    32 => BitConverter.ToInt32(buffer, offset) / 2147483648.0f,
                    _ => 0.0f,
                };
            }

            private static int ReadInt24(byte[] buffer, int offset)
            {
                int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((sample & 0x800000) != 0)
                {
                    sample |= unchecked((int)0xFF000000);
                }

                return sample;
            }

            private static void WriteSample(byte[] buffer, int offset, WaveFormat format, float value)
            {
                if (offset < 0 || offset + BytesPerSample(format) > buffer.Length)
                {
                    return;
                }

                value = Math.Clamp(value, -1.0f, 1.0f);
                if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                        buffer.AsSpan(offset, sizeof(float)), BitConverter.SingleToInt32Bits(value));
                }
                else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
                {
                    short sample = (short)Math.Clamp(value * short.MaxValue, (float)short.MinValue, short.MaxValue);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                        buffer.AsSpan(offset, sizeof(short)), sample);
                }
                else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
                {
                    int sample = (int)Math.Clamp(value * 8388607.0f, -8388608.0f, 8388607.0f);
                    buffer[offset] = (byte)(sample & 0xFF);
                    buffer[offset + 1] = (byte)((sample >> 8) & 0xFF);
                    buffer[offset + 2] = (byte)((sample >> 16) & 0xFF);
                }
                else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
                {
                    int sample = (int)Math.Clamp(value * int.MaxValue, (float)int.MinValue, int.MaxValue);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                        buffer.AsSpan(offset, sizeof(int)), sample);
                }
            }

            public void Dispose()
            {
                try
                {
                    output.Stop();
                }
                catch { }

                output.Dispose();
            }
        }
    }
}
