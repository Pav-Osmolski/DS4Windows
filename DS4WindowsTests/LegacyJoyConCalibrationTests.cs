using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public class LegacyJoyConCalibrationTests
{
    [TestMethod]
    public void SpiReplyMustMatchTheRequestAndContainAllCalibrationBytes()
    {
        byte[] request = [0x46, 0x60, 0, 0, 9];
        byte[] reply = new byte[29];
        reply[0] = 0x21;
        reply[13] = 0x90;
        reply[14] = 0x10;
        request.CopyTo(reply.AsSpan(15));

        Assert.IsTrue(JoyConDevice.MatchesCalibrationReply(reply, request));
        Assert.IsFalse(JoyConDevice.MatchesCalibrationReply(default, request));
        Assert.IsFalse(JoyConDevice.MatchesCalibrationReply(reply.AsSpan(0, 28), request));

        reply[15] = 0x3D;
        Assert.IsFalse(JoyConDevice.MatchesCalibrationReply(reply, request));
        reply[15] = 0x46;
        reply[13] = 0;
        Assert.IsFalse(JoyConDevice.MatchesCalibrationReply(reply, request));
        reply[13] = 0x90;
        reply[0] = 0x30;
        Assert.IsFalse(JoyConDevice.MatchesCalibrationReply(reply, request));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void StickCalibrationUsesTheCorrectSideLayoutAndRejectsInvalidRanges(bool left)
    {
        byte[] center = [0x00, 0x08, 0x80];
        byte[] below = [0xE8, 0x83, 0x3E];
        byte[] above = [0xB0, 0x04, 0x4B];
        byte[] record = left ? [.. above, .. center, .. below] : [.. center, .. below, .. above];

        Assert.IsTrue(JoyConDevice.TryDecodeStickCalibration(record, left, user: true, out var x, out var y));
        Assert.AreEqual((ushort)1048, x.min);
        Assert.AreEqual((ushort)2048, x.mid);
        Assert.AreEqual((ushort)3248, y.max);
        Assert.IsFalse(JoyConDevice.TryDecodeStickCalibration(default, left, user: false, out _, out _));
        Assert.IsFalse(JoyConDevice.TryDecodeStickCalibration(new byte[9], left, user: false, out _, out _));
        Array.Fill(record, (byte)0xFF);
        Assert.IsFalse(JoyConDevice.TryDecodeStickCalibration(record, left, user: false, out _, out _));
    }
}
