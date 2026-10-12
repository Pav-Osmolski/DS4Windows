using System.Runtime.CompilerServices;
using DS4Windows.DS4Control;
using FakerInputWrapper;

namespace DS4WindowsTests;

[TestClass]
public class CalibrationMouseOutputTests
{
    [TestMethod]
    [DataRow(0, 0, 0, 0, false)]
    [DataRow(7, 9, 20, 0, false)]
    [DataRow(32767, -32767, 0, 0, false)]
    [DataRow(32767, -32767, -32767, 32767, false)]
    [DataRow(32767, 0, 18000, 0, true)]
    [DataRow(-32767, 0, -18000, 0, true)]
    [DataRow(32767, 9, 18000, -2, true)]
    [DataRow(9, -32767, -2, -18000, true)]
    [DataRow(32767, -32767, 32767, -32767, true)]
    [DataRow(-32767, 32767, -32767, 32767, true)]
    [DataRow(32760, 32760, 7, 8, true)]
    public void SplitsPendingAndCalibrationCountsExactly(int pendingX, int pendingY,
        int x, int y, bool secondExpected)
    {
        var reports = new CalibrationMouseReports(pendingX, pendingY, x, y);
        Assert.AreEqual(pendingX + x, reports.FirstX + reports.SecondX);
        Assert.AreEqual(pendingY + y, reports.FirstY + reports.SecondY);
        Assert.AreEqual(secondExpected, reports.HasSecond);
        foreach (short component in new[]
            { reports.FirstX, reports.FirstY, reports.SecondX, reports.SecondY })
            Assert.IsTrue(component is >= -32767 and <= 32767);
    }

    [TestMethod]
    [DataRow(-32768, 0)]
    [DataRow(32768, 0)]
    [DataRow(0, -32768)]
    [DataRow(0, 32768)]
    [DataRow(int.MinValue, 0)]
    [DataRow(0, int.MaxValue)]
    public void RejectsOutOfRangeBeforePresentingMovement(int x, int y)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CalibrationMouseReports(0, 0, x, y));
        var handler = new RecordingHandler();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            handler.MoveRelativeMouseCalibration(x, y));
        Assert.AreEqual(0, handler.ImmediateCalls);
    }

    [TestMethod]
    public void DefaultBackendDelegatesOneBoundedCalibrationToImmediate()
    {
        var handler = new RecordingHandler();
        handler.MoveRelativeMouseCalibration(32767, -32767);
        Assert.AreEqual(1, handler.ImmediateCalls);
        Assert.AreEqual((32767, -32767), handler.LastMove);
    }

    [TestMethod]
    public void SplitReportsCarryPendingWheelsOnceAndKeepHeldButtons()
    {
        var report = new RelativeMouseReport
        {
            MouseX = 32767,
            MouseY = 9,
            WheelPosition = 3,
            HWheelPosition = unchecked((byte)-2),
        };
        var button = Enum.GetValues<MouseButton>().First(value => (int)value != 0);
        report.ButtonDown(button);
        var reports = new CalibrationMouseReports(report.MouseX, report.MouseY, 18000, -2);
        report.MouseX = reports.FirstX;
        report.MouseY = reports.FirstY;
        Assert.AreEqual((byte)3, report.WheelPosition);
        Assert.AreEqual(unchecked((byte)-2), report.HWheelPosition);
        Assert.IsTrue(report.HeldButtons.Contains(button));
        report.ResetMousePos();
        report.MouseX = reports.SecondX;
        report.MouseY = reports.SecondY;
        Assert.AreEqual((byte)0, report.WheelPosition);
        Assert.AreEqual((byte)0, report.HWheelPosition);
        Assert.IsTrue(report.HeldButtons.Contains(button));
        Assert.AreEqual((short)18000, report.MouseX);
        Assert.AreEqual((short)0, report.MouseY);
        report.ResetMousePos();
        Assert.AreEqual((short)0, report.MouseX);
        Assert.AreEqual((short)0, report.MouseY);
        Assert.IsTrue(report.HeldButtons.Contains(button));
    }

    [TestMethod]
    public void FakerCalibrationEmitsAndClearsPendingMovementInsideOneLock()
    {
        string source = File.ReadAllText(HandlerSourcePath());
        int start = source.IndexOf("public override void MoveRelativeMouseCalibration(",
            StringComparison.Ordinal);
        int end = source.IndexOf("public override void MoveAbsoluteMouse(", start,
            StringComparison.Ordinal);
        string method = source[start..end];
        int enter = method.IndexOf("eventLock.EnterWriteLock();", StringComparison.Ordinal);
        int firstSend = method.IndexOf("fakerInput.UpdateRelativeMouse(mouseReport);",
            StringComparison.Ordinal);
        int firstReset = method.IndexOf("mouseReport.ResetMousePos();", StringComparison.Ordinal);
        int secondSend = method.LastIndexOf("fakerInput.UpdateRelativeMouse(mouseReport);",
            StringComparison.Ordinal);
        int lastReset = method.LastIndexOf("mouseReport.ResetMousePos();", StringComparison.Ordinal);
        int clear = method.IndexOf("syncRelativeMouse = false;", StringComparison.Ordinal);
        int release = method.IndexOf("eventLock.ExitWriteLock();", StringComparison.Ordinal);
        Assert.IsTrue(enter >= 0 && enter < firstSend && firstSend < firstReset &&
            firstReset < secondSend && secondSend < lastReset && lastReset < clear && clear < release);
        StringAssert.Contains(method, "new CalibrationMouseReports(");
        StringAssert.Contains(method, "finally");
    }

    private static string HandlerSourcePath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "DS4Windows",
            "DS4Control", "OutputKBM", "FakerInputHandler.cs"));

    private sealed class RecordingHandler : VirtualKBMBase
    {
        internal int ImmediateCalls;
        internal (int X, int Y) LastMove;
        public override void MoveRelativeMouseImmediate(int x, int y)
        {
            ImmediateCalls++;
            LastMove = (x, y);
        }
        public override bool Connect() => throw new AssertFailedException();
        public override bool Disconnect() => throw new AssertFailedException();
        public override void MoveRelativeMouse(int x, int y) => throw new AssertFailedException();
        public override void MoveAbsoluteMouse(double x, double y) => throw new AssertFailedException();
        public override void PerformMouseWheelEvent(int v, int h) => throw new AssertFailedException();
        public override void PerformMouseButtonEvent(uint button) => throw new AssertFailedException();
        public override void PerformMouseButtonPress(uint button) => throw new AssertFailedException();
        public override void PerformMouseButtonRelease(uint button) => throw new AssertFailedException();
        public override void PerformKeyPress(uint key) => throw new AssertFailedException();
        public override void PerformKeyPressAlt(uint key) => throw new AssertFailedException();
        public override void PerformKeyRelease(uint key) => throw new AssertFailedException();
        public override void PerformKeyReleaseAlt(uint key) => throw new AssertFailedException();
        public override string GetDisplayName() => "calibration-test";
        public override string GetIdentifier() => "calibration-test";
        public override string GetFullDisplayName() => "calibration-test";
    }
}
