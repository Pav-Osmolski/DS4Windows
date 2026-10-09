using DS4WinWPF.DS4Forms.Converters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Windows;

namespace DS4Windows.Tests;

[TestClass]
public class TrayIconResolutionTests
{
    [DataTestMethod]
    [DataRow(16, 16)]
    [DataRow(20, 24)]
    [DataRow(24, 24)]
    [DataRow(28, 32)]
    [DataRow(32, 32)]
    [DataRow(40, 48)]
    [DataRow(48, 48)]
    [DataRow(64, 64)]
    public void RealControllerIconsNeverUpscaleASmallerFrame(int requested, int expected)
    {
        foreach (string name in new[] { "DS4W.ico", "DS4W - White.ico", "DS4W - Black.ico" })
        {
            using Stream stream = Application.GetResourceStream(new Uri(
                "/DS4Windows;component/Resources/" + name, UriKind.Relative)).Stream;
            using var icon = TrayIconLoader.Load(stream, requested);
            Assert.AreEqual(expected, icon.Width, name);
            Assert.AreEqual(expected, icon.Height, name);
            Assert.AreNotEqual(IntPtr.Zero, icon.Handle);
        }
    }

    [TestMethod]
    public void BatteryIconsRemainLoadableWithIndependentNativeHandles()
    {
        for (int percentage = 0; percentage <= 100; percentage += 10)
        {
            using Stream stream = Application.GetResourceStream(new Uri(
                $"/DS4Windows;component/Resources/{percentage}.ico", UriKind.Relative)).Stream;
            using var first = TrayIconLoader.Load(stream, 32);
            stream.Position = 0;
            using var second = TrayIconLoader.Load(stream, 32);
            Assert.AreNotEqual(first.Handle, second.Handle);
            first.Dispose();
            Assert.AreNotEqual(IntPtr.Zero, second.Handle);
        }
    }

    [TestMethod]
    public void InvalidIconDataFailsBeforeNativeHandleCreation()
    {
        using var truncated = new MemoryStream(new byte[] { 0, 0, 1, 0, 1, 0 });
        Assert.ThrowsException<InvalidDataException>(() => TrayIconLoader.Load(truncated, 32));
        using var empty = new MemoryStream();
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => TrayIconLoader.Load(empty, 0));
    }
}
