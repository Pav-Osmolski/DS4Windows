using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using DS4Windows.InputDevices;
using NLog;

namespace DS4Windows.DS4Control
{
    // Wire contract: Paliverse/DSX @ 1614f003d7f00fa501e789c16eacb219993c1c16,
    // Mod System (DSX v3)/Mod System (DSX v3.1+)/DSX_UDP_Example/{Resources,Program}.cs.
    // Legacy RGB/default instruction shapes: Paliverse/DualSenseX official v2
    // UDP example ZIP, Git blob 0487b3af64dd33a09d582172cf0baabbe27c1567.
    // Unknown/unverified effects are rejected, never approximated or passed as
    // arbitrary raw HID commands. This adapter is exclusively for local mods.
    public sealed class DSXUdpServer : IDisposable
    {
        public const int DEFAULT_PORT = 6969;
        public const string DEFAULT_LISTEN_ADDRESS = "127.0.0.1";
        internal const int MaximumPacketBytes = 16384;
        internal const int MaximumInstructions = 64;
        internal const int MaximumParameters = 14;
        internal const int MaximumControllers = 8;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object lifecycle = new object();
        private readonly object delivery = new object();
        private Session current;
        private bool disposed;
        private int port = DEFAULT_PORT;
        private string address = DEFAULT_LISTEN_ADDRESS;
        private string lastError = string.Empty;
        private long nextDiagnostic;

        private sealed class Session
        {
            internal readonly UdpClient Socket;
            internal readonly Socket Receiver;
            internal readonly object Delivery;
            internal readonly PacketBudget Budget = new PacketBudget();
            internal Thread Thread;
            internal volatile bool Stopped;
            internal Session(UdpClient socket, object delivery)
            {
                Socket = socket;
                // Capture before publishing the session. UdpClient.Dispose
                // clears Client, so its Receive can dereference null when
                // shutdown races the framework's disposed check. The exact
                // Socket instead reports disposal through the normal catch.
                Receiver = socket.Client;
                Delivery = delivery;
            }
        }

        // Fixed memory and a global listener budget, not an unbounded sender map.
        internal sealed class PacketBudget
        {
            internal const int Burst = 128;
            internal const int PerSecond = 1000;
            private readonly object gate = new object();
            private double tokens = Burst;
            private long updated;
            internal PacketBudget() : this(Stopwatch.GetTimestamp()) { }
            internal PacketBudget(long timestamp) => updated = timestamp;
            internal bool TryTake(long now)
            {
                lock (gate)
                {
                    double elapsed = Math.Max(0, (now - updated) / (double)Stopwatch.Frequency);
                    tokens = Math.Min(Burst, tokens + elapsed * PerSecond);
                    updated = Math.Max(updated, now);
                    if (tokens < 1) return false;
                    tokens -= 1;
                    return true;
                }
            }
        }

        public bool IsRunning { get { lock (lifecycle) return current != null && !current.Stopped; } }
        public int Port { get { lock (lifecycle) return port; } }
        public string ListenAddress { get { lock (lifecycle) return address; } }
        public string LastError { get { lock (lifecycle) return lastError; } }
        public delegate void TriggerUpdateHandler(int controllerIndex, TriggerId trigger, byte[] rawTriggerData);
        public delegate void RGBUpdateHandler(int controllerIndex, byte r, byte g, byte b, byte brightness);
        public delegate void MicLEDUpdateHandler(int controllerIndex, byte mode);
        public delegate void PlayerLEDUpdateHandler(int controllerIndex, bool[] leds);
        public delegate void ResetUserSettingsHandler(int controllerIndex);
        public delegate DSXStatusResponse StatusRequestHandler();
        public event TriggerUpdateHandler OnTriggerUpdate;
        public event RGBUpdateHandler OnRGBUpdate;
        public event MicLEDUpdateHandler OnMicLEDUpdate;
        public event PlayerLEDUpdateHandler OnPlayerLEDUpdate;
        public event ResetUserSettingsHandler OnResetUserSettings;
        public StatusRequestHandler GetStatus;
        internal Action PacketStarting;
        internal Action PacketCompleted;
        internal Action<string> UnexpectedStopped;

        public static bool TryValidateEndpoint(int port, string listenAddress, out IPAddress ip, out string error)
        {
            ip = null;
            error = string.Empty;
            if (port < 1 || port > ushort.MaxValue)
                error = "Choose a UDP port between 1 and 65535.";
            else if (!IPAddress.TryParse(listenAddress, out ip) || !IPAddress.IsLoopback(ip))
                error = "DSX game mods must use a local loopback address (127.0.0.1 or ::1).";
            return error.Length == 0;
        }

        public bool Start(int port = DEFAULT_PORT, string listenAddress = DEFAULT_LISTEN_ADDRESS)
        {
            if (!TryValidateEndpoint(port, listenAddress, out IPAddress ip, out string error))
            {
                lock (lifecycle) lastError = error;
                return false;
            }
            return StartCore(port, ip);
        }

        internal bool StartForTesting(IPAddress ip = null) => StartCore(0, ip ?? IPAddress.Loopback);
        internal void FailSocketForTesting() { lock (lifecycle) current?.Socket.Dispose(); }

        private bool StartCore(int requestedPort, IPAddress ip)
        {
            Session previous;
            bool started = false;
            lock (lifecycle)
            {
                if (disposed) { lastError = "The DSX listener has been disposed."; return false; }
                previous = DetachLocked();
                UdpClient socket = null;
                try
                {
                    socket = new UdpClient(ip.AddressFamily);
                    socket.ExclusiveAddressUse = true;
                    socket.Client.Bind(new IPEndPoint(ip, requestedPort));
                    var session = new Session(socket, delivery);
                    session.Thread = new Thread(() => ReceiveLoop(session))
                    { IsBackground = true, Name = "DSX UDP listener" };
                    current = session;
                    port = ((IPEndPoint)socket.Client.LocalEndPoint).Port;
                    address = ip.ToString();
                    lastError = string.Empty;
                    session.Thread.Start();
                    started = true;
                }
                catch (Exception error) when (error is SocketException || error is ArgumentException ||
                    error is InvalidOperationException || error is System.Threading.ThreadStateException)
                {
                    current = null;
                    socket?.Dispose();
                    lastError = error is SocketException socketError && socketError.SocketErrorCode == SocketError.AddressAlreadyInUse
                        ? "The DSX UDP port is already in use. Close the other listener or choose another port, then retry."
                        : "The DSX UDP listener could not bind the selected local endpoint.";
                    Diagnostic(error, "DSX UDP listener startup failed.");
                }
            }
            Quiesce(previous);
            return started;
        }

        public void Stop()
        {
            Session previous;
            lock (lifecycle) { previous = DetachLocked(); lastError = string.Empty; }
            Quiesce(previous);
        }

        private Session DetachLocked()
        {
            Session previous = current;
            current = null;
            if (previous != null)
            {
                previous.Stopped = true;
                previous.Socket.Dispose(); // Interrupt only this generation's Receive.
            }
            return previous;
        }

        private static void Quiesce(Session session)
        {
            if (session == null || session.Thread == Thread.CurrentThread || Monitor.IsEntered(session.Delivery)) return;
            // Never wait under lifecycle or from the callback's own thread.
            // After this barrier no callback from this session remains.
            lock (session.Delivery) { }
            session.Thread?.Join(2000);
        }

        private void ReceiveLoop(Session session)
        {
            string failure = null;
            try
            {
                byte[] buffer = new byte[ushort.MaxValue + 1];
                EndPoint peer = new IPEndPoint(session.Receiver.AddressFamily == AddressFamily.InterNetworkV6
                    ? IPAddress.IPv6Any : IPAddress.Any, 0);
                while (!session.Stopped)
                {
                    int received = session.Receiver.ReceiveFrom(buffer, ref peer);
                    byte[] data = new byte[received];
                    Buffer.BlockCopy(buffer, 0, data, 0, received);
                    ProcessPacket(session, data, (IPEndPoint)peer);
                }
            }
            catch (Exception error) when (error is SocketException || error is ObjectDisposedException)
            {
                if (!session.Stopped)
                {
                    failure = "The DSX UDP listener stopped unexpectedly. Apply / Retry to restart it.";
                    Diagnostic(error, "DSX UDP listener stopped unexpectedly.");
                }
            }
            finally
            {
                bool notify = false;
                lock (lifecycle)
                {
                    session.Stopped = true;
                    if (ReferenceEquals(current, session))
                    {
                        current = null;
                        if (failure != null) { lastError = failure; notify = true; }
                    }
                }
                session.Socket.Dispose();
                if (notify)
                {
                    // Revoke only this listener's owner after admitted callbacks
                    // drain. A successor may be created by the notification.
                    lock (session.Delivery) { }
                    try { UnexpectedStopped?.Invoke(failure); }
                    catch (Exception error) { Diagnostic(error, "DSX UDP stopped notification failed."); }
                }
            }
        }

        // All ingress uses a live generation. Pure parsing cannot call devices.
        public bool ProcessIncomingPacket(byte[] data, IPEndPoint peer)
        {
            Session session;
            lock (lifecycle) session = current;
            return session != null && ProcessPacket(session, data, peer);
        }

        private bool ProcessPacket(Session session, byte[] data, IPEndPoint peer)
        {
            if (session.Stopped || peer == null || !IPAddress.IsLoopback(peer.Address) || peer.Port == 0 ||
                !session.Budget.TryTake(Stopwatch.GetTimestamp()) || !TryParsePacket(data, out DSXInstruction[] instructions))
                return false;
            lock (session.Delivery)
            {
                if (session.Stopped) return false;
                try
                {
                    PacketStarting?.Invoke();
                    foreach (DSXInstruction instruction in instructions)
                    {
                        if (instruction.Unsupported == null) continue;
                        if (!session.Stopped) SendStatus(session, peer, instruction.Unsupported);
                        return false; // Unsupported batches cannot partially alter a controller.
                    }
                    foreach (DSXInstruction instruction in instructions)
                    {
                        if (session.Stopped) return false;
                        Dispatch(instruction);
                    }
                    if (!session.Stopped) SendStatus(session, peer);
                    return !session.Stopped;
                }
                catch (Exception error)
                {
                    Diagnostic(error, "DSX UDP callback failed.");
                    return false;
                }
                finally
                {
                    try { PacketCompleted?.Invoke(); }
                    catch (Exception error) { Diagnostic(error, "DSX UDP packet completion failed."); }
                }
            }
        }

        private void Dispatch(DSXInstruction instruction)
        {
            byte[] values = instruction.Data;
            int index = instruction.ControllerIndex;
            switch (instruction.Type)
            {
                case 1: OnTriggerUpdate?.Invoke(index, instruction.Trigger, (byte[])values.Clone()); break;
                case 2: OnRGBUpdate?.Invoke(index, values[0], values[1], values[2], values[3]); break;
                case 3:
                case 6:
                    var leds = new bool[5];
                    for (int i = 0; i < leds.Length; i++) leds[i] = values[i] != 0;
                    OnPlayerLEDUpdate?.Invoke(index, leds);
                    break;
                case 5: OnMicLEDUpdate?.Invoke(index, values[0]); break;
                case 7: OnResetUserSettings?.Invoke(index); break;
            }
        }

        private void SendStatus(Session session, IPEndPoint peer, string unsupported = null)
        {
            DSXStatusResponse response = BoundStatus(GetStatus?.Invoke());
            if (unsupported != null) response.Status = unsupported;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(response);
            if (!session.Stopped) session.Socket.Send(bytes, bytes.Length, peer);
        }

        internal static DSXStatusResponse BoundStatus(DSXStatusResponse supplied)
        {
            var result = new DSXStatusResponse
            {
                Status = "DS4Windows DSX UDP Server Running",
                TimeReceived = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            var seen = new HashSet<int>();
            if (supplied?.Devices != null)
            {
                for (int i = 0; i < Math.Min(MaximumControllers, supplied.Devices.Count); i++)
                {
                    DSXDeviceInfo device = supplied.Devices[i];
                    if (device == null || device.Index < 0 || device.Index >= MaximumControllers ||
                        device.DeviceType < 0 || device.DeviceType > 7 || !seen.Add(device.Index)) continue;
                    result.Devices.Add(new DSXDeviceInfo
                    {
                        Index = device.Index, MacAddress = device.MacAddress?.Length <= 64 ? device.MacAddress : string.Empty,
                        DeviceType = device.DeviceType, ConnectionType = Math.Clamp(device.ConnectionType, 0, 2),
                        BatteryLevel = Math.Clamp(device.BatteryLevel, 0, 100), IsSupportAT = device.IsSupportAT,
                        IsSupportLightBar = device.IsSupportLightBar, IsSupportPlayerLED = device.IsSupportPlayerLED,
                        IsSupportLegacyPlayerLED = device.IsSupportLegacyPlayerLED, IsSupportMicLED = device.IsSupportMicLED,
                    });
                }
            }
            result.isControllerConnected = result.Devices.Count != 0;
            result.BatteryLevel = result.isControllerConnected ? result.Devices[0].BatteryLevel : 0;
            return result;
        }

        private void Diagnostic(Exception error, string message)
        {
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref nextDiagnostic);
            if (now >= previous && Interlocked.CompareExchange(ref nextDiagnostic,
                    now + Stopwatch.Frequency * 5, previous) == previous)
                Logger.Debug(error, message); // No packet contents or per-datagram log spam.
        }

        internal sealed record DSXInstruction(int Type, int ControllerIndex, TriggerId Trigger, byte[] Data,
            string Unsupported = null);

        internal static bool TryParsePacket(byte[] data, out DSXInstruction[] instructions)
        {
            instructions = Array.Empty<DSXInstruction>();
            if (data == null || data.Length == 0 || data.Length > MaximumPacketBytes) return false;
            try
            {
                using JsonDocument document = JsonDocument.Parse(StrictUtf8.GetString(data),
                    new JsonDocumentOptions { MaxDepth = 8 });
                JsonElement root = document.RootElement;
                if (!HasExactProperties(root, "instructions") ||
                    !root.TryGetProperty("instructions", out JsonElement array) || array.ValueKind != JsonValueKind.Array ||
                    array.GetArrayLength() == 0 || array.GetArrayLength() > MaximumInstructions) return false;
                var parsed = new List<DSXInstruction>(array.GetArrayLength());
                foreach (JsonElement element in array.EnumerateArray())
                {
                    if (!TryParseInstruction(element, out DSXInstruction instruction)) return false;
                    parsed.Add(instruction);
                }
                instructions = parsed.ToArray();
                return true;
            }
            catch (Exception error) when (error is JsonException || error is DecoderFallbackException ||
                error is InvalidOperationException || error is FormatException || error is OverflowException)
            { return false; }
        }

        private static bool HasExactProperties(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
                if (Array.IndexOf(names, property.Name) < 0 || !seen.Add(property.Name)) return false;
            return seen.Count == names.Length;
        }

        private static bool TryParseInstruction(JsonElement element, out DSXInstruction instruction)
        {
            instruction = null;
            if (!HasExactProperties(element, "type", "parameters")) return false;
            JsonElement typeValue = element.GetProperty("type");
            int type;
            if (typeValue.ValueKind == JsonValueKind.Number)
            { if (!typeValue.TryGetInt32(out type)) return false; }
            else if (typeValue.ValueKind == JsonValueKind.String)
            {
                type = typeValue.GetString()?.ToLowerInvariant() switch
                {
                    "getdsxstatus" => 0, "triggerupdate" => 1, "rgbupdate" => 2, "playerled" => 3,
                    "triggerthreshold" => 4, "micled" => 5, "playerlednewrevision" => 6, "resettousersettings" => 7, _ => -1,
                };
            }
            else return false;
            if (type < 0 || type > 7) return false;
            JsonElement parameters = element.GetProperty("parameters");
            // Legacy fixed-size instruction arrays serialize unused entries as
            // type 0 with null parameters. They are harmless status/no-op entries,
            // not permission to coerce null mutation parameters or missing fields.
            if (type == 0 && parameters.ValueKind == JsonValueKind.Null)
            {
                instruction = new DSXInstruction(type, 0, default, Array.Empty<byte>());
                return true;
            }
            if (parameters.ValueKind != JsonValueKind.Array || parameters.GetArrayLength() > MaximumParameters) return false;
            int count = parameters.GetArrayLength();
            if (type == 0)
            {
                if (count != 0) return false;
                instruction = new DSXInstruction(type, 0, default, Array.Empty<byte>());
                return true;
            }
            var values = new int[count];
            for (int i = 0; i < count; i++)
            {
                JsonElement value = parameters[i];
                if (type == 3 && i > 0 && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                    values[i] = value.GetBoolean() ? 1 : 0;
                else if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out values[i])) return false;
            }
            if (count == 0 || values[0] < 0 || values[0] >= MaximumControllers) return false;
            int index = values[0];
            byte[] bytes;
            switch (type)
            {
                case 1:
                    if (count < 3 || (values[1] != 1 && values[1] != 2)) return false;
                    if (IsUnsupportedTrigger(values[2], values.AsSpan(3)))
                    {
                        instruction = new DSXInstruction(type, index, default, Array.Empty<byte>(),
                            "Unsupported DSX legacy trigger mode: " + values[2].ToString(CultureInfo.InvariantCulture));
                        return true;
                    }
                    if (!TryEncodeTriggerPayload(values[2], values.AsSpan(3), out bytes)) return false;
                    instruction = new DSXInstruction(type, index, values[1] == 1 ? TriggerId.LeftTrigger : TriggerId.RightTrigger, bytes);
                    return true;
                case 2:
                    if ((count != 4 && count != 5) || !AllBetween(values.AsSpan(1), 0, 255)) return false;
                    // DSX v2 RGB has no brightness field; v3 adds one explicitly.
                    bytes = new[] { (byte)values[1], (byte)values[2], (byte)values[3], count == 5 ? (byte)values[4] : (byte)255 };
                    break;
                case 3:
                    if (count != 6 || !AllBetween(values.AsSpan(1), 0, 1)) return false;
                    bytes = new byte[5];
                    for (int i = 0; i < 5; i++) bytes[i] = (byte)values[i + 1];
                    break;
                case 5:
                    if (count != 2 || values[1] < 0 || values[1] > 2) return false;
                    bytes = new[] { (byte)values[1] };
                    break;
                case 4:
                    if (count != 3 || (values[1] != 1 && values[1] != 2) || values[2] < 0 || values[2] > 255) return false;
                    instruction = new DSXInstruction(type, index, default, Array.Empty<byte>(), values[2] == 0 ? null :
                        "Unsupported DSX nonzero trigger threshold; profile input mapping was not changed.");
                    return true;
                case 6:
                    if (count != 2 || values[1] < 0 || values[1] > 5) return false;
                    int mask = new[] { 0x04, 0x0A, 0x15, 0x1B, 0x1F, 0x00 }[values[1]];
                    bytes = new byte[5];
                    for (int i = 0; i < 5; i++) bytes[i] = (byte)((mask >> i) & 1);
                    break;
                case 7:
                    if (count != 1) return false;
                    bytes = Array.Empty<byte>();
                    break;
                default: return false;
            }
            instruction = new DSXInstruction(type, index, default, bytes);
            return true;
        }

        private static bool AllBetween(ReadOnlySpan<int> values, int minimum, int maximum)
        {
            foreach (int value in values) if (value < minimum || value > maximum) return false;
            return true;
        }

        private static bool IsUnsupportedTrigger(int mode, ReadOnlySpan<int> parameters)
        {
            if (!AllBetween(parameters, 0, 255)) return false;
            return mode switch
            {
                2 or 3 or 4 or 5 or 6 or 7 or 9 or 10 or 11 or 19 => parameters.Length == 0,
                8 => parameters.Length == 1,
                12 => parameters.Length == 8 && parameters[0] >= 9 && parameters[0] <= 16,
                _ => false,
            };
        }

        public static byte[] EncodeTriggerPayload(int mode, byte[] parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            int[] values = Array.ConvertAll(parameters, value => (int)value);
            return TryEncodeTriggerPayload(mode, values, out byte[] bytes) ? bytes :
                throw new ArgumentException("Unsupported trigger mode or invalid parameter count/range.", nameof(parameters));
        }

        // Bit packing follows the documented DualSense effect format. Reference:
        // https://gist.github.com/Nielk1/6d54cc2c00d2201ccb8c2720ad7538db
        // Copyright (c) 2021-2022 John "Nielk1" Klein, MIT License.
        // Permission is hereby granted, free of charge, to any person obtaining
        // a copy of this software and associated documentation files (the
        // "Software"), to deal in the Software without restriction, including
        // without limitation the rights to use, copy, modify, merge, publish,
        // distribute, sublicense, and/or sell copies of the Software, and to
        // permit persons to whom the Software is furnished to do so, subject to
        // the following conditions: The above copyright notice and this
        // permission notice shall be included in all copies or substantial
        // portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT
        // WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
        // THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE
        // AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
        // HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
        // IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
        // IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        internal static bool TryEncodeTriggerPayload(int mode, ReadOnlySpan<int> p, out byte[] bytes)
        {
            bytes = null;
            if (!AllBetween(p, 0, 255)) return false;
            var result = new byte[11];
            result[0] = 0x05;
            switch (mode)
            {
                case 0:
                case 20:
                    if (p.Length != 0) return false;
                    break;
                case 1:
                    // Numeric compatibility evidence, independently encoded:
                    // WujekFoliarz/DualSenseY @ 6787d099f752b43c24a74cda2e8a881f40311c8a,
                    // MainWindow.xaml.cs UDP GameCube preset: Pulse, 144,160,255.
                    if (p.Length != 0) return false;
                    result[0] = 0x02; result[1] = 144; result[2] = 160; result[3] = 255;
                    break;
                case 12:
                    // DSX submode names map to the verified trigger mode table
                    // in Wujek-Dualsense-API TriggerType.cs. Controller.cs places
                    // its seven fields at offsets 1..6 and 9, identically on USB
                    // and Bluetooth. Encode those numeric protocol facts only;
                    // never admit calibration (0xFC) or an arbitrary mode byte.
                    if (p.Length != 8 || p[0] > 8) return false;
                    result[0] = new byte[] { 0x00, 0x01, 0x21, 0x05, 0x25, 0x02, 0x22, 0x06, 0x26 }[p[0]];
                    for (int i = 1; i <= 6; i++) result[i] = (byte)p[i];
                    result[9] = (byte)p[7];
                    break;
                case 13:
                case 21:
                    if (p.Length != 2 || p[0] > 9 || p[1] > 8) return false;
                    if (p[1] != 0) EncodeZones(result, 0x21, p[0], p[1], 0);
                    break;
                case 16:
                case 22:
                    if (p.Length != 3 || p[0] < 2 || p[0] > 7 || p[1] <= p[0] || p[1] > 8 || p[2] > 8) return false;
                    if (p[2] != 0) { EncodeEndpoints(result, 0x25, p[0], p[1]); result[3] = (byte)(p[2] - 1); }
                    break;
                case 17:
                case 23:
                    if (p.Length != 3 || p[0] > 9 || p[1] > 8) return false;
                    if (p[1] != 0 && p[2] != 0) EncodeZones(result, 0x26, p[0], p[1], p[2]);
                    break;
                case 14:
                    if (p.Length != 4 || p[0] > 7 || p[1] <= p[0] || p[1] > 8 || p[2] > 8 || p[3] > 8) return false;
                    if (p[2] != 0 && p[3] != 0)
                    { EncodeEndpoints(result, 0x22, p[0], p[1]); result[3] = (byte)((p[2] - 1) | ((p[3] - 1) << 3)); }
                    break;
                case 15:
                    if (p.Length != 5 || p[0] > 8 || p[1] <= p[0] || p[1] > 9 || p[2] > 6 || p[3] <= p[2] || p[3] > 7) return false;
                    if (p[4] != 0)
                    { EncodeEndpoints(result, 0x23, p[0], p[1]); result[3] = (byte)(p[3] | (p[2] << 3)); result[4] = (byte)p[4]; }
                    break;
                case 18:
                    if (p.Length != 6 || p[0] > 8 || p[1] <= p[0] || p[1] > 9 || p[2] > 7 || p[3] > 7 || p[5] > 2) return false;
                    if (p[4] != 0)
                    { EncodeEndpoints(result, 0x27, p[0], p[1]); result[3] = (byte)(p[2] | (p[3] << 3)); result[4] = (byte)p[4]; result[5] = (byte)p[5]; }
                    break;
                case 24:
                    if (p.Length != 4 || p[0] > 8 || p[1] <= p[0] || p[1] > 9 || p[2] < 1 || p[2] > 8 || p[3] < 1 || p[3] > 8) return false;
                    var strengths = new int[10];
                    float slope = (p[3] - p[2]) / (float)(p[1] - p[0]);
                    for (int i = p[0]; i < 10; i++) strengths[i] = i <= p[1] ? (int)Math.Round(p[2] + slope * (i - p[0])) : p[3];
                    EncodeRegions(result, 0x21, strengths, 0);
                    break;
                case 25:
                    if (p.Length != 10 || !AllBetween(p, 0, 8)) return false;
                    EncodeRegions(result, 0x21, p, 0);
                    break;
                case 26:
                    if (p.Length != 11 || !AllBetween(p.Slice(1), 0, 8)) return false;
                    if (p[0] != 0) EncodeRegions(result, 0x26, p.Slice(1), p[0]);
                    break;
                default: return false;
            }
            bytes = result;
            return true;
        }

        private static void EncodeEndpoints(byte[] bytes, byte mode, int start, int end)
        {
            int mask = (1 << start) | (1 << end);
            bytes[0] = mode; bytes[1] = (byte)mask; bytes[2] = (byte)(mask >> 8);
        }

        private static void EncodeZones(byte[] bytes, byte mode, int start, int strength, int frequency)
        {
            Span<int> regions = stackalloc int[10];
            regions.Clear();
            regions.Slice(start).Fill(strength);
            EncodeRegions(bytes, mode, regions, frequency);
        }

        private static void EncodeRegions(byte[] bytes, byte mode, ReadOnlySpan<int> regions, int frequency)
        {
            uint packed = 0;
            int mask = 0;
            for (int i = 0; i < 10; i++)
            {
                if (regions[i] == 0) continue;
                mask |= 1 << i;
                packed |= (uint)(regions[i] - 1) << (3 * i);
            }
            if (mask == 0) return;
            bytes[0] = mode; bytes[1] = (byte)mask; bytes[2] = (byte)(mask >> 8);
            for (int i = 0; i < 4; i++) bytes[3 + i] = (byte)(packed >> (i * 8));
            bytes[9] = (byte)frequency;
        }

        public void Dispose()
        {
            Session previous;
            lock (lifecycle) { disposed = true; previous = DetachLocked(); }
            Quiesce(previous);
        }
    }

    public sealed class DSXStatusResponse
    {
        public string Status { get; set; } = "Running";
        public string TimeReceived { get; set; } = string.Empty;
        public bool isControllerConnected { get; set; }
        public int BatteryLevel { get; set; }
        public List<DSXDeviceInfo> Devices { get; set; } = new List<DSXDeviceInfo>();
    }

    public sealed class DSXDeviceInfo
    {
        public int Index { get; set; }
        public string MacAddress { get; set; } = string.Empty;
        public int DeviceType { get; set; }
        public int ConnectionType { get; set; }
        public int BatteryLevel { get; set; }
        public bool IsSupportAT { get; set; }
        public bool IsSupportLightBar { get; set; }
        public bool IsSupportPlayerLED { get; set; }
        public bool IsSupportLegacyPlayerLED { get; set; }
        public bool IsSupportMicLED { get; set; }
    }
}
