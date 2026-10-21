using System.Diagnostics;
using System.Globalization;
using DS4Windows;
using DS4Windows.Switch2;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ControllerReportIntervalDisplayTests
{
    [TestMethod]
    [DataRow("en-US", "4.00", "1.23")]
    [DataRow("ru-RU", "4,00", "1,23")]
    [DataRow("ja-JP", "4.00", "1.23")]
    [DataRow("zh-Hans", "4.00", "1.23")]
    public void FormatsTwoDecimalPlacesAcrossTranslatedResources(string cultureName, string whole, string fractional)
    {
        var oldCulture = CultureInfo.CurrentCulture;
        var oldResources = DS4WinWPF.Properties.Resources.Culture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            DS4WinWPF.Properties.Resources.Culture = culture;
            var display = new ControllerReportIntervalDisplay();
            var device = CreateDevice();
            SetInterval(device, 4);
            Assert.IsTrue(display.TryUpdate(device, 0, out string text));
            StringAssert.Contains(text, whole);
            SetInterval(device, 1.23456789);
            Assert.IsTrue(display.TryUpdate(device, Stopwatch.Frequency, out text));
            StringAssert.Contains(text, fractional);
            Assert.IsFalse(text.Contains("23456789"));
            Assert.AreEqual(1.23456789, device.Latency, "Display rounding must not change the measurement.");
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            DS4WinWPF.Properties.Resources.Culture = oldResources;
        }
    }

    [TestMethod]
    public void RefreshesAt250MillisecondsWithoutReplayingSkippedValues()
    {
        var display = new ControllerReportIntervalDisplay();
        var device = CreateDevice();
        SetInterval(device, 4);
        Assert.IsTrue(display.TryUpdate(device, 0, out _));
        for (int milliseconds = 1; milliseconds < 250; milliseconds++)
        {
            SetInterval(device, milliseconds);
            Assert.IsFalse(display.TryUpdate(device, Stopwatch.Frequency * milliseconds / 1000, out _));
        }
        SetInterval(device, 7.89);
        Assert.IsTrue(display.TryUpdate(device, Stopwatch.Frequency / 4, out string text));
        StringAssert.Contains(text, 7.89.ToString("0.00", CultureInfo.CurrentCulture));
        Assert.IsFalse(display.TryUpdate(device, Stopwatch.Frequency / 4 + 1, out _));
        SetInterval(device, 2.34);
        Assert.IsTrue(display.TryUpdate(device, Stopwatch.Frequency * 20, out text));
        StringAssert.Contains(text, 2.34.ToString("0.00", CultureInfo.CurrentCulture));
    }

    [TestMethod]
    public void SwitchingOrResumingImmediatelyDisplaysTheCurrentDevice()
    {
        var display = new ControllerReportIntervalDisplay();
        var first = CreateDevice();
        var second = CreateDevice();
        Assert.IsTrue(display.TryUpdate(first, 0, out _));
        Assert.IsTrue(display.TryUpdate(second, 1, out _));
        Assert.IsFalse(display.TryUpdate(second, 2, out _));
        display.Reset();
        Assert.IsTrue(display.TryUpdate(second, 3, out _));
    }

    private static void SetInterval(DS4Device device, double interval) =>
        typeof(DS4Device).GetProperty(nameof(DS4Device.Latency))!.SetValue(device, interval);

    private static DS4Device CreateDevice()
    {
        Assert.IsTrue(Switch2RuntimeInputDevice.TryCreatePro(98_001, 98_002,
            Switch2Transport.Usb, out var device, out var failure), failure.ToString());
        return device;
    }
}
