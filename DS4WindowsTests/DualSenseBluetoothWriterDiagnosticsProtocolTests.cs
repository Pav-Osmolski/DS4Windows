using DS4Windows.InputDevices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DS4Windows.Tests
{
    [TestClass]
    public class DualSenseBluetoothWriterDiagnosticsProtocolTests
    {
        [TestMethod]
        public void HelperAcknowledgementPreservesLegacyMetricsAndPublishesPhaseTimings()
        {
            // No process, pipe, controller or runtime service is started.
            var pacer = (DualSenseBluetoothAudioPacer)RuntimeHelpers.GetUninitializedObject(
                typeof(DualSenseBluetoothAudioPacer));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DualSenseBluetoothAudioPacer).GetField("stateLock", flags)
                .SetValue(pacer, new object());
            typeof(DualSenseBluetoothAudioPacer).GetField("outstandingReports", flags)
                .SetValue(pacer, new Dictionary<long, byte>());

            const int headerLength = sizeof(long) + sizeof(byte) + sizeof(long);
            byte[] payload = new byte[headerLength + 21 * sizeof(long)];
            long[] metrics = new long[21];
            metrics[6] = 10; // Existing completed-write metric.
            metrics[8] = Stopwatch.Frequency * 2;
            metrics[17] = 2;
            metrics[18] = Stopwatch.Frequency * 2;
            metrics[19] = 8;
            metrics[20] = Stopwatch.Frequency / 100;
            for (int index = 0; index < metrics.Length; index++)
                BinaryPrimitives.WriteInt64LittleEndian(
                    payload.AsSpan(headerLength + index * sizeof(long)), metrics[index]);

            typeof(DualSenseBluetoothAudioPacer).GetMethod("ProcessAcknowledgement", flags)
                .Invoke(pacer, new object[] { payload });

            Assert.AreEqual(10L, pacer.HelperCompletedWriteCount);
            Assert.AreEqual(2000.0, pacer.HelperMaximumCompletionMilliseconds, 0.01);
            Assert.AreEqual(2L, pacer.HelperStartupCompletedWrites);
            Assert.AreEqual(2000.0, pacer.HelperStartupMaximumCompletionMilliseconds, 0.01);
            Assert.AreEqual(8L, pacer.HelperStreamingCompletedWrites);
            Assert.AreEqual(10.0, pacer.HelperStreamingMaximumCompletionMilliseconds, 0.01);
        }
    }
}
