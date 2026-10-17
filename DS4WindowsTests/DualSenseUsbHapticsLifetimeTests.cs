using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows.InputDevices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public sealed class DualSenseUsbHapticsLifetimeTests
{
    [TestMethod]
    public async Task StopThenStartWaitsForTheActualRetiringAudioOwner()
    {
        var firstStarted = Signal();
        var firstCancelled = Signal();
        var releaseCleanup = Signal();
        var secondStarted = Signal();
        int starts = 0;
        var owner = new DualSenseUsbHapticsPassthrough(async token =>
        {
            int current = Interlocked.Increment(ref starts);
            (current == 1 ? firstStarted : secondStarted).TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                if (current == 1)
                {
                    firstCancelled.TrySetResult();
                    await releaseCleanup.Task;
                }
            }
        });
        var device = Device(ConnectionType.USB);
        try
        {
            owner.Update(0, device, true, "", null);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Stop(0);
            await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            owner.Update(0, device, true, "", null);
            Assert.AreEqual(1, Volatile.Read(ref starts));
            Assert.IsFalse(secondStarted.Task.IsCompleted,
                "A successor must not open audio while old capture disposal is pending.");
            releaseCleanup.TrySetResult();
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            owner.StopAll();
            await owner.Retirement(0).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { releaseCleanup.TrySetResult(); owner.StopAll(); }
    }

    [TestMethod]
    public async Task ReapplyingSameRouteDoesNotRestartButEndpointChangesDo()
    {
        var started = Signal();
        int starts = 0;
        var owner = new DualSenseUsbHapticsPassthrough(async token =>
        {
            Interlocked.Increment(ref starts);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var device = Device(ConnectionType.USB);
        try
        {
            owner.Update(0, device, true, "one", null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task original = owner.Retirement(0);
            owner.Update(0, device, true, "one", null);
            Assert.AreSame(original, owner.Retirement(0));
            owner.Update(0, device, true, "two", null);
            Assert.AreNotSame(original, owner.Retirement(0));
            owner.StopAll();
            await owner.Retirement(0).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { owner.StopAll(); }
    }

    [TestMethod]
    public void BluetoothAndDisabledFeedbackNeverOpenUsbAudio()
    {
        var owner = new DualSenseUsbHapticsPassthrough(_ =>
            throw new AssertFailedException("The USB route was started for an ineligible controller."));
        owner.Update(0, Device(ConnectionType.BT), true, "", null);
        owner.Update(1, Device(ConnectionType.USB), false, "", null);
        Assert.IsTrue(owner.Retirement(0).IsCompletedSuccessfully);
        Assert.IsTrue(owner.Retirement(1).IsCompletedSuccessfully);
    }

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static DualSenseDevice Device(ConnectionType type)
    {
        // Exercise the real route owner without opening HID, WASAPI or VIIPER.
        var device = (DualSenseDevice)RuntimeHelpers.GetUninitializedObject(typeof(DualSenseDevice));
        typeof(DS4Device).GetField("conType", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(device, type);
        return device;
    }
}
