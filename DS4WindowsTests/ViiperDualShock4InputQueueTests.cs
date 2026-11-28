
using System.Diagnostics;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class ViiperDualShock4InputQueueTests
{
    [TestMethod]
    public void AShortPressAndReleaseSurviveCoalescedMotionAndWriterDeferral()
    {
        var queue = new ViiperDualShock4InputQueue();
        long now = Stopwatch.GetTimestamp();
        byte[] press = Packet(cross: true);
        byte[] release = Packet(cross: false);
        byte[] latest = Packet(cross: false, x: 238);
        queue.Publish(press, now);
        Assert.AreSame(press, queue.Peek(now, out _));
        queue.Publish(Packet(cross: true, x: 166), now);
        queue.Publish(release, now);
        queue.Publish(latest, now);

        Assert.AreSame(press, queue.Peek(now, out _));
        queue.Commit(press);
        Assert.AreSame(release, queue.Peek(now, out _));
        queue.Commit(release);
        Assert.AreSame(latest, queue.Peek(now, out _));
        queue.Commit(latest);
        Assert.IsNull(queue.Peek(now, out _));
    }

    [TestMethod]
    public void StaleOrOverflowedHistoryResumesWithCurrentState()
    {
        var queue = new ViiperDualShock4InputQueue();
        long now = Stopwatch.GetTimestamp();
        queue.Publish(Packet(cross: true), now);
        byte[] release = Packet(cross: false);
        queue.Publish(release, now);
        Assert.AreSame(release, queue.Peek(now + Stopwatch.Frequency, out _));
        queue.Commit(release);
        Assert.IsNull(queue.Peek(now + Stopwatch.Frequency, out _));

        queue.Clear();
        for (int index = 0; index < 64; index++)
            queue.Publish(Packet(index % 2 != 0), now);
        Assert.IsTrue(queue.Publish(release, now));
        Assert.AreSame(release, queue.Peek(now, out _));
        queue.Commit(release);
        Assert.IsNull(queue.Peek(now, out _));
    }

    private static byte[] Packet(bool cross, byte x = 128) =>
        ViiperStatePacketBuilder.Build(ViiperVirtualDeviceType.DualShock4,
            new DS4State { Cross = cross, LX = x }, -1);
}
