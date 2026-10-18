using System.IO;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public sealed class AudioHapticsFrequencyFilterTests
{
    [TestMethod]
    [DataRow(8000)]
    [DataRow(44100)]
    [DataRow(48000)]
    [DataRow(96000)]
    public void CustomBandPassRetainsBassAndRejectsSubBassAndVoiceDetail(int sampleRate)
    {
        var settings = new AudioHapticsProfileSettings
        {
            CustomFrequencyFilters = true, LowPassHz = 160, HighPassHz = 40,
            Response = AudioHapticsResponse.Subtle,
        };
        double bass = Rms(settings, sampleRate, 90);
        Assert.IsTrue(bass > 0.03, $"Bass output too low: {bass}");
        Assert.IsTrue(Rms(settings, sampleRate, 5) < bass * 0.08);
        Assert.IsTrue(Rms(settings, sampleRate, 1000) < bass * 0.02);
    }

    [TestMethod]
    public void ZeroHighPassAllowsLowFrequenciesAndIntensityStillControlsStrength()
    {
        var settings = new AudioHapticsProfileSettings
        {
            CustomFrequencyFilters = true, LowPassHz = 160, HighPassHz = 0,
            GainPercent = 50, Response = AudioHapticsResponse.Subtle,
        };
        double lowGain = Rms(settings, 48000, 10);
        settings.GainPercent = 150;
        Assert.IsTrue(Rms(settings, 48000, 10) > lowGain * 2);
        settings.HighPassHz = 100;
        Assert.IsTrue(Rms(settings, 48000, 10) < lowGain * 0.1);
    }

    [TestMethod]
    public void DisabledCustomFiltersPreserveEveryLegacySampleAndIgnoreStoredCutoffs()
    {
        var original = new AudioHapticsProcessor(new(), 48000);
        var disabled = new AudioHapticsProcessor(new()
        {
            CustomFrequencyFilters = false, LowPassHz = 1400, HighPassHz = 200,
        }, 48000);
        for (int i = 0; i < 48000; i++)
        {
            float input = (float)(0.3 * Math.Sin(i * 0.017) + 0.1 * Math.Cos(i * 0.3));
            original.Process(input, -input, out float left, out float right);
            disabled.Process(input, -input, out float otherLeft, out float otherRight);
            Assert.AreEqual(left, otherLeft);
            Assert.AreEqual(right, otherRight);
        }
    }

    [TestMethod]
    [DataRow(40, 200)]
    [DataRow(1400, 0)]
    public void ExtremeCutoffsRemainFiniteBoundedAndStereoIndependent(int low, int high)
    {
        var settings = new AudioHapticsProfileSettings
        {
            CustomFrequencyFilters = true, LowPassHz = low, HighPassHz = high,
            GainPercent = 200, Response = AudioHapticsResponse.Strong,
        };
        var processor = new AudioHapticsProcessor(settings, 8000);
        for (int i = 0; i < 16000; i++)
        {
            processor.Process(i % 2 == 0 ? 1 : -1, 0, out float left, out float right);
            Assert.IsTrue(float.IsFinite(left) && Math.Abs(left) <= 1);
            Assert.AreEqual(0f, right);
        }
    }

    [TestMethod]
    public void ProfileNormalizationAndClonePreserveAValidPassBand()
    {
        var settings = new AudioHapticsProfileSettings
        {
            CustomFrequencyFilters = true, LowPassHz = -20, HighPassHz = int.MaxValue,
        }.Clone();
        Assert.IsTrue(settings.CustomFrequencyFilters);
        Assert.AreEqual(40, settings.LowPassHz);
        Assert.AreEqual(30, settings.HighPassHz);
        Assert.IsFalse(settings.IsDefaultConfiguration());
        settings.LowPassHz = int.MaxValue;
        settings.HighPassHz = -1;
        settings.Normalize();
        Assert.AreEqual(1400, settings.LowPassHz);
        Assert.AreEqual(0, settings.HighPassHz);
    }

    [TestMethod]
    public void OlderProfileWithoutFilterFieldsKeepsLegacyResponse()
    {
        var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
        // Use the real serializer's root and defaults, as old profiles omit only the new fields.
        using var writer = new StringWriter();
        serializer.Serialize(writer, new ProfileDTO { AudioHapticsSettings = new() { Enabled = true } });
        var xml = System.Xml.Linq.XDocument.Parse(writer.ToString());
        foreach (var element in xml.Descendants().Where(e => e.Name.LocalName is "CustomFrequencyFilters" or "LowPassHz" or "HighPassHz").ToArray())
            element.Remove();
        using var oldReader = new StringReader(xml.ToString());
        var restored = (ProfileDTO)serializer.Deserialize(oldReader);
        Assert.IsFalse(restored.AudioHapticsSettings.CustomFrequencyFilters);
        Assert.AreEqual(160, restored.AudioHapticsSettings.LowPassHz);
        Assert.AreEqual(20, restored.AudioHapticsSettings.HighPassHz);
    }

    private static double Rms(AudioHapticsProfileSettings settings, int rate, double frequency)
    {
        var processor = new AudioHapticsProcessor(settings, rate);
        double sum = 0;
        for (int i = 0; i < rate; i++)
        {
            float value = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / rate));
            processor.Process(value, value, out float left, out _);
            if (i >= rate / 2) sum += left * left;
        }
        return Math.Sqrt(sum / (rate - rate / 2));
    }
}
