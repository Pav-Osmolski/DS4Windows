using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DS4Windows.DS4Control;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public sealed class DSXUdpServerTests
{
    public static IEnumerable<object[]> GoldenVectors()
    {
        yield return new object[] { 0, Array.Empty<byte>(), "0500000000000000000000" };
        yield return new object[] { 20, Array.Empty<byte>(), "0500000000000000000000" };
        yield return new object[] { 1, Array.Empty<byte>(), "0290A0FF00000000000000" };
        var customModes = new byte[] { 0x00, 0x01, 0x21, 0x05, 0x25, 0x02, 0x22, 0x06, 0x26 };
        for (byte submode = 0; submode < customModes.Length; submode++)
            yield return new object[] { 12, new byte[] { submode, 1, 2, 3, 4, 5, 6, 7 }, customModes[submode].ToString("X2") + "01020304050600000700" };
        foreach (int mode in new[] { 13, 21 })
            yield return new object[] { mode, new byte[] { 2, 6 }, "21FC0340DBB62D00000000" };
        foreach (int mode in new[] { 16, 22 })
            yield return new object[] { mode, new byte[] { 2, 6, 5 }, "2544000400000000000000" };
        foreach (int mode in new[] { 17, 23 })
            yield return new object[] { mode, new byte[] { 0, 5, 25 }, "26FF032449922400001900" };
        yield return new object[] { 14, new byte[] { 1, 7, 5, 4 }, "2282001C00000000000000" };
        yield return new object[] { 15, new byte[] { 0, 8, 1, 3, 12 }, "2301010B0C000000000000" };
        yield return new object[] { 18, new byte[] { 1, 8, 2, 5, 15, 2 }, "2702012A0F020000000000" };
        yield return new object[] { 24, new byte[] { 1, 9, 8, 1 }, "21FE03B8CB4D0100000000" };
        yield return new object[] { 25, new byte[] { 8, 8, 8, 0, 0, 0, 8, 8, 0, 0 }, "21C700FF01FC0000000000" };
        // Official DSX order is frequency FIRST, followed by ten amplitudes.
        yield return new object[] { 26, new byte[] { 10, 8, 8, 8, 8, 8, 0, 0, 0, 8, 8 }, "261F03FF7F003F00000A00" };
    }

    [TestMethod]
    [DynamicData(nameof(GoldenVectors), DynamicDataSourceType.Method)]
    public void VerifiedTriggerModesMatchAllElevenGoldenBytes(int mode, byte[] parameters, string expected)
    {
        CollectionAssert.AreEqual(Convert.FromHexString(expected), DSXUdpServer.EncodeTriggerPayload(mode, parameters));
    }

    [TestMethod]
    [DataRow(13, "[0,0]")]
    [DataRow(16, "[2,7,0]")]
    [DataRow(14, "[1,7,0,4]")]
    [DataRow(14, "[1,7,4,0]")]
    [DataRow(15, "[0,8,1,3,0]")]
    [DataRow(18, "[1,8,2,5,0,2]")]
    [DataRow(23, "[0,0,25]")]
    [DataRow(23, "[0,5,0]")]
    [DataRow(25, "[0,0,0,0,0,0,0,0,0,0]")]
    [DataRow(26, "[0,8,8,8,8,8,8,8,8,8,8]")]
    [DataRow(26, "[10,0,0,0,0,0,0,0,0,0,0]")]
    public void ZeroEffectsReleaseInsteadOfClampingToActiveResistance(int mode, string parameters)
    {
        Assert.IsTrue(DSXUdpServer.TryEncodeTriggerPayload(mode, JsonSerializer.Deserialize<int[]>(parameters), out byte[] bytes));
        CollectionAssert.AreEqual(Convert.FromHexString("0500000000000000000000"), bytes);
    }

    [TestMethod]
    [DataRow(21, "[]")]
    [DataRow(21, "[2]")]
    [DataRow(21, "[2,6,9]")]
    [DataRow(21, "[-1,6]")]
    [DataRow(21, "[10,6]")]
    [DataRow(21, "[2,9]")]
    [DataRow(22, "[1,6,5]")]
    [DataRow(22, "[7,7,5]")]
    [DataRow(22, "[7,9,5]")]
    [DataRow(14, "[8,8,5,4]")]
    [DataRow(15, "[0,8,3,3,12]")]
    [DataRow(18, "[1,8,8,5,15,2]")]
    [DataRow(18, "[1,8,2,5,15,3]")]
    [DataRow(23, "[0,5,256]")]
    [DataRow(24, "[5,5,8,1]")]
    [DataRow(24, "[1,9,0,1]")]
    [DataRow(25, "[8,8,8]")]
    [DataRow(26, "[10,8,8,8,8,8,8,8,8,8]")]
    [DataRow(26, "[10,8,8,8,8,9,0,0,0,8,8]")]
    [DataRow(999, "[]")]
    [DataRow(-1, "[]")]
    [DataRow(12, "[9,0,101,255,255,0,0,0]")]
    [DataRow(12, "[252,0,0,0,0,0,0,0]")]
    [DataRow(12, "[8,0,101,255,255,0,0]")]
    public void InvalidOrUnverifiedEffectsNeverProduceFallbackBytes(int mode, string parameters)
    {
        Assert.IsFalse(DSXUdpServer.TryEncodeTriggerPayload(mode, JsonSerializer.Deserialize<int[]>(parameters), out byte[] bytes));
        Assert.IsNull(bytes);
    }

    [TestMethod]
    public void OfficialWireSidesAndControllerIndicesAreNotZeroBasedTriggerEnums()
    {
        var commands = Parse("{\"type\":1,\"parameters\":[7,1,22,2,6,5]}",
            "{\"type\":\"TriggerUpdate\",\"parameters\":[0,2,20]}");
        Assert.AreEqual(7, commands[0].ControllerIndex);
        Assert.AreEqual(TriggerId.LeftTrigger, commands[0].Trigger);
        Assert.AreEqual(0, commands[1].ControllerIndex);
        Assert.AreEqual(TriggerId.RightTrigger, commands[1].Trigger);
    }

    [TestMethod]
    [DataRow(0, 0x04)]
    [DataRow(1, 0x0A)]
    [DataRow(2, 0x15)]
    [DataRow(3, 0x1B)]
    [DataRow(4, 0x1F)]
    [DataRow(5, 0x00)]
    public void NewPlayerLedRevisionDecodesTheEnumNotBooleanPositionOne(int value, int expectedMask)
    {
        var command = Parse($"{{\"type\":6,\"parameters\":[0,{value}]}}")[0];
        int mask = 0;
        for (int i = 0; i < 5; i++) mask |= command.Data[i] << i;
        Assert.AreEqual(expectedMask, mask);
    }

    [TestMethod]
    public void LegacyLedBooleansBrightnessAndMicPulseRetainExactMeaning()
    {
        var commands = Parse("{\"type\":3,\"parameters\":[0,true,0,true,0,1]}",
            "{\"type\":2,\"parameters\":[0,255,100,50,0]}", "{\"type\":5,\"parameters\":[0,1]}");
        CollectionAssert.AreEqual(new byte[] { 1, 0, 1, 0, 1 }, commands[0].Data);
        CollectionAssert.AreEqual(new byte[] { 255, 100, 50, 0 }, commands[1].Data);
        CollectionAssert.AreEqual(new byte[] { 1 }, commands[2].Data);
    }

    [TestMethod]
    public void OfficialV2RgbDefaultsOnlyTheOmittedBrightnessToFull()
    {
        var commands = Parse("{\"type\":2,\"parameters\":[0,255,100,50]}",
            "{\"type\":2,\"parameters\":[0,255,100,50,0]}");
        CollectionAssert.AreEqual(new byte[] { 255, 100, 50, 255 }, commands[0].Data);
        CollectionAssert.AreEqual(new byte[] { 255, 100, 50, 0 }, commands[1].Data);
    }

    [TestMethod]
    public void LegacyNullStatusIsAcceptedWithoutCoercingNullMutationParameters()
    {
        var command = Parse("{\"type\":0,\"parameters\":null}")[0];
        Assert.AreEqual(0, command.Type);
        Assert.AreEqual(0, command.Data.Length);
        for (int type = 1; type <= 7; type++)
            Assert.IsFalse(DSXUdpServer.TryParsePacket(Packet($"{{\"type\":{type},\"parameters\":null}}"), out _));
        Assert.IsFalse(DSXUdpServer.TryParsePacket(Packet("{\"type\":0}"), out _));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"type\":7}")]
    [DataRow("{\"type\":7,\"parameters\":[]}")]
    [DataRow("{\"type\":7,\"parameters\":[null]}")]
    [DataRow("{\"type\":7,\"parameters\":[true]}")]
    [DataRow("{\"type\":7,\"parameters\":[\"0\"]}")]
    [DataRow("{\"type\":7,\"parameters\":[0.5]}")]
    [DataRow("{\"type\":7,\"parameters\":[-1]}")]
    [DataRow("{\"type\":7,\"parameters\":[8]}")]
    [DataRow("{\"type\":7,\"parameters\":[2147483648]}")]
    [DataRow("{\"type\":7,\"parameters\":[1e99]}")]
    [DataRow("{\"type\":\"unknown\",\"parameters\":[]}")]
    [DataRow("{\"type\":null,\"parameters\":[]}")]
    [DataRow("{\"type\":true,\"parameters\":[]}")]
    [DataRow("{\"type\":0.5,\"parameters\":[]}")]
    [DataRow("{\"type\":8,\"parameters\":[]}")]
    [DataRow("{\"type\":7,\"type\":0,\"parameters\":[]}")]
    [DataRow("{\"type\":7,\"parameters\":[0],\"parameters\":[1]}")]
    [DataRow("{\"type\":7,\"parameters\":[0],\"extra\":1}")]
    [DataRow("{\"type\":7,\"parameters\":[{}]}")]
    [DataRow("{\"type\":7,\"parameters\":[[]]}")]
    [DataRow("{\"type\":1,\"parameters\":[0,0,21,2,6]}")]
    [DataRow("{\"type\":1,\"parameters\":[0,3,21,2,6]}")]
    [DataRow("{\"type\":1,\"parameters\":[0,1,999]}")]
    [DataRow("{\"type\":1,\"parameters\":[0,1,21,\"2,bad,6\"]}")]
    [DataRow("{\"type\":1,\"parameters\":[0,1,21,2,true]}")]
    [DataRow("{\"type\":2,\"parameters\":[0,255,0]}")]
    [DataRow("{\"type\":2,\"parameters\":[0,256,0,0,255]}")]
    [DataRow("{\"type\":3,\"parameters\":[0,1]}")]
    [DataRow("{\"type\":3,\"parameters\":[0,2,0,0,0,0]}")]
    [DataRow("{\"type\":5,\"parameters\":[0,3]}")]
    [DataRow("{\"type\":6,\"parameters\":[0,6]}")]
    [DataRow("{\"type\":4,\"parameters\":[0,0,0]}")]
    [DataRow("{\"type\":0,\"parameters\":[0]}")]
    [DataRow("null")]
    [DataRow("[]")]
    public void InvalidInstructionRejectsTheWholePacketBeforeAnyPartialCommands(string invalid)
    {
        byte[] packet = Packet("{\"type\":7,\"parameters\":[0]}", invalid);
        Assert.IsFalse(DSXUdpServer.TryParsePacket(packet, out var commands));
        Assert.AreEqual(0, commands.Length);
    }

    [TestMethod]
    public void PacketGrammarDepthUtf8AndSizeAreBounded()
    {
        foreach (string text in new[] { "", "{}", "[]", "{\"instructions\":[]}",
            "{\"type\":7,\"parameters\":[0]}", "{\"instructions\":[],\"instructions\":[]}",
            "{\"instructions\":[[[[[[[[[[0]]]]]]]]]]}" })
            Assert.IsFalse(DSXUdpServer.TryParsePacket(Encoding.UTF8.GetBytes(text), out _), text);
        Assert.IsFalse(DSXUdpServer.TryParsePacket(new byte[] { 0xFF, 0xFE }, out _));
        Assert.IsFalse(DSXUdpServer.TryParsePacket(new byte[DSXUdpServer.MaximumPacketBytes + 1], out _));
        Assert.IsFalse(DSXUdpServer.TryParsePacket(null, out _));
        var instruction = "{\"type\":0,\"parameters\":[]}";
        Assert.IsTrue(DSXUdpServer.TryParsePacket(Packet(Enumerable.Repeat(instruction, DSXUdpServer.MaximumInstructions).ToArray()), out _));
        Assert.IsFalse(DSXUdpServer.TryParsePacket(Packet(Enumerable.Repeat(instruction, DSXUdpServer.MaximumInstructions + 1).ToArray()), out _));
        Assert.IsFalse(DSXUdpServer.TryParsePacket(Packet("{\"type\":1,\"parameters\":[" +
            string.Join(',', Enumerable.Repeat(1, DSXUdpServer.MaximumParameters + 1)) + "]}"), out _));
    }

    [TestMethod]
    public void DeterministicMalformedCorpusNeverEscapesParserOrCreatesFallbackEffects()
    {
        var random = new Random(86);
        for (int i = 0; i < 2000; i++)
        {
            var bytes = new byte[random.Next(0, 512)];
            random.NextBytes(bytes);
            Assert.IsFalse(DSXUdpServer.TryParsePacket(bytes, out var commands));
            Assert.AreEqual(0, commands.Length);
        }
    }

    [TestMethod]
    public void FloodBudgetHasBoundedBurstAndRefillsWithoutAnUnboundedSenderTable()
    {
        var budget = new DSXUdpServer.PacketBudget(0);
        for (int i = 0; i < DSXUdpServer.PacketBudget.Burst; i++) Assert.IsTrue(budget.TryTake(0));
        for (int i = 0; i < 10000; i++) Assert.IsFalse(budget.TryTake(0));
        Assert.IsTrue(budget.TryTake(Stopwatch.Frequency));
        for (int i = 1; i < DSXUdpServer.PacketBudget.Burst; i++) Assert.IsTrue(budget.TryTake(Stopwatch.Frequency));
        Assert.IsFalse(budget.TryTake(Stopwatch.Frequency));
        Assert.IsFalse(budget.TryTake(0)); // A backward timestamp cannot replenish tokens.
    }

    [TestMethod]
    [DataRow(0, "127.0.0.1", false)]
    [DataRow(-1, "127.0.0.1", false)]
    [DataRow(65536, "127.0.0.1", false)]
    [DataRow(6969, "0.0.0.0", false)]
    [DataRow(6969, "::", false)]
    [DataRow(6969, "192.168.1.5", false)]
    [DataRow(6969, "255.255.255.255", false)]
    [DataRow(6969, "not-an-address", false)]
    [DataRow(6969, null, false)]
    [DataRow(6969, "127.0.0.1", true)]
    [DataRow(65535, "::1", true)]
    public void PublicListenerNeverBindsALanOrEphemeralEndpoint(int port, string address, bool expected)
    {
        Assert.AreEqual(expected, DSXUdpServer.TryValidateEndpoint(port, address, out _, out string error));
        Assert.AreEqual(expected, error.Length == 0);
    }

    [TestMethod]
    public void StatusDoesNotInventConnectedDevicesBatteryOrCapabilities()
    {
        var empty = DSXUdpServer.BoundStatus(null);
        Assert.IsFalse(empty.isControllerConnected);
        Assert.AreEqual(0, empty.BatteryLevel);
        Assert.AreEqual(0, empty.Devices.Count);
        var status = DSXUdpServer.BoundStatus(new DSXStatusResponse
        {
            isControllerConnected = true, BatteryLevel = 100,
            Devices = new() { new() { Index = 0, BatteryLevel = 0 }, new() { Index = 1, BatteryLevel = 80 } },
        });
        Assert.IsTrue(status.isControllerConnected);
        Assert.AreEqual(0, status.BatteryLevel);
        Assert.IsFalse(status.Devices[0].IsSupportAT);
        Assert.IsFalse(status.Devices[0].IsSupportMicLED);
    }

    [TestMethod]
    public void StatusResponseSizeAndDevicesAreBounded()
    {
        var supplied = new DSXStatusResponse { Status = new string('x', 100000), TimeReceived = new string('x', 100000) };
        for (int i = 0; i < 1000; i++) supplied.Devices.Add(new DSXDeviceInfo
        { Index = i, MacAddress = new string('x', 10000), BatteryLevel = 1000 });
        var result = DSXUdpServer.BoundStatus(supplied);
        Assert.AreEqual(8, result.Devices.Count);
        Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(result).Length < 4096);
    }

    private static DSXUdpServer.DSXInstruction[] Parse(params string[] instructions)
    {
        Assert.IsTrue(DSXUdpServer.TryParsePacket(Packet(instructions), out var parsed));
        return parsed;
    }

    internal static byte[] Packet(params string[] instructions) => Encoding.UTF8.GetBytes(
        "{\"instructions\":[" + string.Join(',', instructions) + "]}");
}
