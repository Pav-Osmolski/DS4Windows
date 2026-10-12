using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows.InputDevices;

namespace DS4WindowsTests;

[TestClass]
public sealed class DualSenseBluetoothLocalTriggerProofTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AdmittedNativeOrResetCommandAllowsTheSameLocalEffectAgain(bool control)
    {
        using var fixture = new Fixture();
        byte[] local = Report(0x08, 0x26);
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        Assert.AreEqual(1, fixture.Drain().Count, "Repeating a local generation without intervention remains deduplicated.");

        Assert.IsTrue(fixture.Intervene(Report(0x08, 0x05), control));
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));

        var commands = fixture.Drain();
        Assert.AreEqual(2, commands.Count, "The reset/native command and reasserted local effect must both be queued.");
        Assert.AreEqual(control ? DualSenseBluetoothAudioPacer.MessageKind.QueueReport :
            DualSenseBluetoothAudioPacer.MessageKind.UpdateGameStateAndTemplate, commands[0].Kind);
        Assert.AreEqual(DualSenseBluetoothAudioPacer.MessageKind.UpdateControllerState, commands[1].Kind);
        CollectionAssert.AreEqual(local.Skip(13).Take(47).ToArray(), commands[1].Payload);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RejectedInterventionDoesNotInvalidateAnAcceptedLocalCommand(bool control)
    {
        using var fixture = new Fixture();
        byte[] local = Report(0x08, 0x26);
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        fixture.Drain();
        fixture.FillQueue();

        Assert.IsFalse(fixture.Intervene(Report(0x08, 0x05), control));
        Assert.AreEqual(0, fixture.Credits.Count, "Rejected native admission must also return its reserved credit.");
        fixture.Drain();

        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        Assert.AreEqual(0, fixture.Drain().Count, "A rejected reset never changed the admitted trigger state.");
    }

    [TestMethod]
    [DataRow(false, (byte)0)]
    [DataRow(true, (byte)0)]
    [DataRow(false, (byte)0x04)]
    [DataRow(true, (byte)0x04)]
    public void VisualOrOppositeTriggerCommandsPreserveUnchangedLocalProof(bool control, byte validity)
    {
        using var fixture = new Fixture();
        byte[] local = Report(0x08, 0x26);
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        fixture.Drain();

        Assert.IsTrue(fixture.Intervene(Report(validity, 0x05), control));
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        Assert.AreEqual(1, fixture.Drain().Count, "A left-only local command remains current after unrelated deltas.");
    }

    [TestMethod]
    public void IdenticalNativePacketsRemainSeparateExactCommands()
    {
        using var fixture = new Fixture();
        byte[] native = Report(0x08, 0x26);
        Assert.IsTrue(fixture.Intervene(native, control: false));
        Assert.IsTrue(fixture.Intervene(native, control: false));
        var commands = fixture.Drain();
        Assert.AreEqual(2, commands.Count);
        Assert.AreEqual(2, fixture.Credits.Count);
        foreach (var command in commands)
        {
            Assert.AreEqual(DualSenseBluetoothAudioPacer.MessageKind.UpdateGameStateAndTemplate, command.Kind);
            CollectionAssert.AreEqual(native.Skip(13).Take(47).ToArray(), command.Payload.Take(47).ToArray());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedLocalReassertionRetainsTheNeedToRetryAfterAnAdmittedReset(bool control)
    {
        using var fixture = new Fixture();
        byte[] local = Report(0x08, 0x26);
        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        Assert.IsTrue(fixture.Intervene(Report(0x08, 0x05), control));
        fixture.Drain();
        fixture.FillQueue();
        Assert.IsFalse(fixture.Pacer.UpdateControllerState(local));
        fixture.Drain();

        Assert.IsTrue(fixture.Pacer.UpdateControllerState(local));
        var commands = fixture.Drain();
        Assert.AreEqual(1, commands.Count);
        CollectionAssert.AreEqual(local.Skip(13).Take(47).ToArray(), commands[0].Payload);
    }

    private static byte[] Report(byte validity, byte mode)
    {
        byte[] report = new byte[DualSenseBluetoothAudioPacer.ReportLength];
        report[0] = 0x36;
        report[11] = 0x90;
        report[12] = 63;
        report[13] = validity;
        report[14] = validity == 0 ? (byte)0x04 : (byte)0;
        report[23] = mode;
        report[34] = mode;
        report[35] = 0xFF;
        report[57] = 17;
        report[76] = 0x92;
        report[77] = 64;
        return report;
    }

    // Exercise the real admission methods/rings without a constructor, process,
    // pipe, helper, controller, worker, or physical output transport.
    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal readonly DualSenseBluetoothAudioPacer Pacer =
            (DualSenseBluetoothAudioPacer)RuntimeHelpers.GetUninitializedObject(typeof(DualSenseBluetoothAudioPacer));
        internal readonly DualSenseNativeCommandCredits Credits = new(32);
        private readonly object gate = new();
        private readonly DualSenseBluetoothAudioPacerRing<DualSenseBluetoothAudioPacer.OutboundCommand> queue = new(80);
        private readonly DualSenseBluetoothAudioPacerPayloadPool pool = new(96, 1024);
        private readonly DualSenseBluetoothAudioPacer.ControlReportCompletionPool completions;
        private readonly AutoResetEvent available = new(false);

        internal Fixture()
        {
            completions = new(gate, 8);
            Set("stateLock", gate);
            Set("outboundCommands", queue);
            Set("outboundPayloads", pool);
            Set("outboundAvailable", available);
            Set("nativeCommandCredits", Credits);
            Set("outstandingReports", new Dictionary<long, byte>());
            Set("controlReportCompletions", completions);
            Set("latestControllerState", new byte[47]);
            Set("latestAdmittedNativeState", new byte[47]);
            Set("realtimeHapticsGeneration", 1);
            Set("currentEpoch", 1);
        }

        internal bool Intervene(byte[] report, bool control)
        {
            if (control) return Pacer.TryQueueControlReport(report, long.MaxValue, out _);
            byte[] quiescent = (byte[])report.Clone();
            quiescent[13] &= unchecked((byte)~0x0C);
            return Pacer.UpdateGameStateAndTemplate(report, quiescent, long.MaxValue);
        }

        internal void FillQueue()
        {
            lock (gate)
            {
                int count = 0;
                while (pool.TryRent(16, out var lease))
                {
                    var command = new DualSenseBluetoothAudioPacer.OutboundCommand(
                        DualSenseBluetoothAudioPacer.MessageKind.UpdateCadence, lease, 16);
                    if (!queue.TryEnqueue(command)) { pool.Return(lease); break; }
                    count++;
                }
                Assert.AreEqual(80, count);
                Assert.IsTrue(pool.AvailableCount > 0, "Exercise queue rejection, not an exhausted payload allocator.");
            }
        }

        internal List<(DualSenseBluetoothAudioPacer.MessageKind Kind, byte[] Payload)> Drain()
        {
            var result = new List<(DualSenseBluetoothAudioPacer.MessageKind, byte[])>();
            lock (gate)
            {
                while (queue.TryDequeue(out var command))
                {
                    result.Add((command.Kind, command.Payload.Buffer.Take(command.PayloadLength).ToArray()));
                    pool.Return(command.Payload);
                }
            }
            return result;
        }

        private void Set(string name, object value) => typeof(DualSenseBluetoothAudioPacer)
            .GetField(name, Private)!.SetValue(Pacer, value);

        public void Dispose()
        {
            Drain();
            completions.Dispose();
            available.Dispose();
        }
    }
}
