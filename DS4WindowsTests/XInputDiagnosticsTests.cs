using System.Runtime.InteropServices;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
public sealed class XInputDiagnosticsTests
{
    [TestMethod]
    public void NativeStructuresMatchWindowsSdkLayout()
    {
        Assert.AreEqual(12, Marshal.SizeOf<XInputDiagnosticGamepad>());
        Assert.AreEqual(16, Marshal.SizeOf<XInputDiagnosticState>());
        Assert.AreEqual(4, Marshal.OffsetOf<XInputDiagnosticState>("Gamepad").ToInt32());
        Assert.AreEqual(4, Marshal.OffsetOf<XInputDiagnosticGamepad>("LeftX").ToInt32());
        Assert.AreEqual(10, Marshal.OffsetOf<XInputDiagnosticGamepad>("RightY").ToInt32());
    }

    [TestMethod]
    public void PollsFourSlotsAndThrottlesDisconnectedOrFailedSlots()
    {
        int[] calls = new int[4];
        bool reconnected = false;
        uint Read(uint index, out XInputDiagnosticState state)
        {
            calls[index]++;
            state = new() { Gamepad = new() { LeftTrigger = 255 } };
            return index == 0 || reconnected ? 0u : index == 3 ? 5u : XInputDiagnosticPoller.NotConnected;
        }
        var poller = new XInputDiagnosticPoller(Read);
        var first = poller.Poll(0);
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, calls);
        Assert.AreEqual((byte)255, first[0].State.Gamepad.LeftTrigger);
        Assert.AreEqual((byte)0, first[1].State.Gamepad.LeftTrigger, "Never expose an output buffer from a failed native read.");
        Assert.AreEqual(5u, first[3].ErrorCode);
        poller.Poll(50);
        CollectionAssert.AreEqual(new[] { 2, 1, 1, 1 }, calls);
        reconnected = true;
        var next = poller.Poll(1000);
        CollectionAssert.AreEqual(new[] { 3, 2, 2, 2 }, calls);
        Assert.IsTrue(next.All(reading => reading.ErrorCode == 0));
        Assert.AreEqual(XInputDiagnosticPoller.NotConnected, first[1].ErrorCode, "Published samples must not be mutated by subsequent polls.");
    }

    [TestMethod]
    public void LiveValuesIncludeAxisExtremesAndKnownButtonsThenClearOnDisconnect()
    {
        var slot = new XInputDiagnosticSlot(0);
        slot.Apply(new(0, new() { Gamepad = new()
        {
            Buttons = 0xFFFF, LeftTrigger = 255, RightTrigger = 123,
            LeftX = short.MinValue, LeftY = short.MaxValue, RightX = -1, RightY = 1,
        } }));
        Assert.IsTrue(slot.Connected);
        Assert.AreEqual("Connected", slot.Status);
        Assert.AreEqual("X: -32768   Y: 32767", slot.LeftStick);
        Assert.AreEqual("X: -1   Y: 1", slot.RightStick);
        Assert.AreEqual(255, slot.LeftTrigger);
        Assert.AreEqual(123, slot.RightTrigger);
        Assert.AreEqual("A  /  B  /  X  /  Y  /  LB  /  RB  /  Back  /  Start  /  L3  /  R3  /  D-pad Up  /  D-pad Down  /  D-pad Left  /  D-pad Right", slot.PressedButtons);
        slot.Apply(new(XInputDiagnosticPoller.NotConnected, default));
        Assert.IsFalse(slot.Connected);
        Assert.AreEqual("Not connected", slot.Status);
        Assert.AreEqual(0, slot.LeftTrigger);
        Assert.AreEqual("X: 0   Y: 0", slot.RightStick);
        Assert.AreEqual("No buttons pressed", slot.PressedButtons);
        slot.Apply(new(5, default));
        Assert.AreEqual("Read failed (Windows error 5)", slot.Status);
    }

    [TestMethod]
    public async Task MissingWindowsApiShowsFailureAndDoesNotContinuouslyRetry()
    {
        int calls = 0;
        uint Read(uint index, out XInputDiagnosticState state)
        {
            calls++; state = default; throw new DllNotFoundException();
        }
        var vm = new XInputDiagnosticsViewModel(new(Read));
        await vm.PollAsync();
        Assert.AreEqual(1, calls);
        StringAssert.Contains(vm.Error, "XInput is unavailable");
        Assert.IsTrue(vm.Slots.All(slot => !slot.Connected && slot.Status == "Unavailable"));
        await vm.PollAsync();
        Assert.AreEqual(1, calls);
        vm.Close();
    }

    [TestMethod]
    public async Task CloseDiscardsPendingResultsAndPreventsOverlappingOrLaterPolls()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        uint Read(uint index, out XInputDiagnosticState state)
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            state = default; return 0;
        }
        var vm = new XInputDiagnosticsViewModel(new(Read));
        Task pending = vm.PollAsync();
        try
        {
            Assert.IsTrue(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))));
            await vm.PollAsync();
            Assert.AreEqual(1, calls, "A second UI tick must not overlap a pending native read.");
            vm.Close();
        }
        finally { release.Set(); }
        await pending;
        Assert.IsTrue(vm.Slots.All(slot => slot.Status == "Checking..."), "A late completion must not update a closed window.");
        int completedCalls = calls;
        await vm.PollAsync();
        Assert.AreEqual(completedCalls, calls);
    }
}
