using System.Diagnostics;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public class ViiperFeedbackPathDiagnosticsTests
{
    [TestMethod]
    public void DisabledVerboseDoesNotCollectOrSchedule()
    {
        var diagnostics = new ViiperFeedbackPathDiagnostics();
        for (int i = 0; i < 100; i++) Assert.IsFalse(diagnostics.Observe(false, 0x84, i + 1));
        StringAssert.Contains(diagnostics.Format(1000), "rearHapticsFrames=0");
        Assert.IsTrue(diagnostics.Observe(true, 0x85, 1000));
        StringAssert.Contains(diagnostics.Format(1000), "microphoneStateFrames=1");
        StringAssert.Contains(diagnostics.Format(1000), "lastControlAgeMs=-1");
    }
    [TestMethod]
    public void CountsDistinguishGameControlMediaAndMicrophoneStatusAndAreThrottled()
    {
        var diagnostics = new ViiperFeedbackPathDiagnostics();
        Assert.IsTrue(diagnostics.Observe(true, 0x85, 1));
        foreach (byte type in new byte[] { 0x81, 0x82, 0x83, 0x84, 0x99 }) Assert.IsFalse(diagnostics.Observe(true, type, 2));
        string text = diagnostics.Format(3);
        foreach (string field in new[] { "controlFrames=1", "speakerFrames=1", "atomicFrames=1", "rearHapticsFrames=1", "microphoneStateFrames=1", "otherFrames=1" }) StringAssert.Contains(text, field);
        Assert.IsTrue(diagnostics.Observe(true, 0x84, Stopwatch.Frequency * 30 + 1));
    }
    [TestMethod]
    public void SilentMediaAndLedOnlyReportsAreNotNonzeroActuatorRequests()
    {
        var diagnostics = new ViiperFeedbackPathDiagnostics();
        byte[] control = new byte[76]; control[28] = 2;
        byte[] rear = new byte[474]; rear[76] = 0x36; rear[152] = 0x92; rear[153] = 64;
        diagnostics.Observe(true, 0x81, 1, control, true);
        diagnostics.Observe(true, 0x84, 2, rear, true);
        control[29] = 3; control[31] = 40; rear[154] = 2;
        diagnostics.Observe(false, 0x81, 3, control, true);
        diagnostics.Observe(false, 0x84, 3, rear, true);
        diagnostics.Observe(true, 0x81, 4, control, false);
        StringAssert.Contains(diagnostics.Format(5), "nonzeroMotorRequests=0");
        StringAssert.Contains(diagnostics.Format(5), "nonzeroRearRequests=0");
        diagnostics.Observe(true, 0x81, 6, control, true);
        diagnostics.Observe(true, 0x84, 6, rear, true);
        StringAssert.Contains(diagnostics.Format(7), "nonzeroMotorRequests=1");
        StringAssert.Contains(diagnostics.Format(7), "nonzeroRearRequests=1");
    }
}
