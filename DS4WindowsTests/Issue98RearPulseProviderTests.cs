using System;
using DS4Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public class Issue98RearPulseProviderTests
{
    [TestMethod]
    public void ControlledSignalIsFiniteRearOnlyAndPreservesBufferBounds()
    {
        var source = new Issue98RearPulseProvider();
        Assert.AreEqual(4, source.WaveFormat.Channels);
        Assert.AreEqual(48000, source.WaveFormat.SampleRate);
        float[] buffer = new float[1003];
        long total = 0, left = 0, right = 0;
        int count;
        do
        {
            Array.Fill(buffer, 99f);
            count = source.Read(buffer, 1, 997); // Deliberately unaligned reads.
            Assert.AreEqual(99f, buffer[0]);
            Assert.AreEqual(99f, buffer[count + 1]);
            for (int index = 0; index < count; index++, total++)
            {
                float value = buffer[index + 1];
                Assert.IsTrue(float.IsFinite(value) && Math.Abs(value) <= 0.150001f);
                if (total % 4 < 2) Assert.AreEqual(0f, value, "Speaker channels must remain silent.");
                if (value != 0 && total % 4 == 2) left++;
                if (value != 0 && total % 4 == 3) right++;
            }
        } while (count != 0);
        Assert.AreEqual((long)Issue98RearPulseProvider.Frames * 4, total);
        Assert.IsTrue(left > 1000 && right > 1000);
        Assert.AreEqual(0, source.Read(buffer, 1, 997), "The test must not stream indefinitely.");
    }
}
