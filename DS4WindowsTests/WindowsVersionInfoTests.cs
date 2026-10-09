using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public class WindowsVersionInfoTests
{
    [TestMethod]
    public void ReportsWindows11DisplayVersionAndFullServicingBuild()
    {
        var details = WindowsVersionInfo.Create("Windows 10 Pro", "26H2", "2009",
            "Client", 26300, 9550, new Version(10, 0, 26300, 0));
        Assert.AreEqual("Windows 11 Pro", details.ProductName);
        Assert.AreEqual("26H2", details.DisplayVersion);
        Assert.AreEqual("26300.9550", details.Build);
        Assert.AreEqual("10.0.26300.9550", details.KernelVersion);
        Assert.AreEqual("OS Version: Windows 11 Pro 26H2 (Build 26300.9550)", details.LogLines[0]);
    }

    [DataTestMethod]
    [DataRow("Windows 10 Pro", "Client", 19045, "Windows 10 Pro")]
    [DataRow("Windows Server 2025 Standard", "Server", 26100, "Windows Server 2025 Standard")]
    [DataRow("Windows 10 Pro", "Server", 26100, "Windows 10 Pro")]
    [DataRow("Windows 11 Pro", "Client", 26300, "Windows 11 Pro")]
    public void PreservesWindows10AndServerProductIdentity(string product, string installation,
        int build, string expected)
    {
        Assert.AreEqual(expected, WindowsVersionInfo.Create(product, "", "2009",
            installation, build, null, new Version(10, 0, build, 0)).ProductName);
    }

    [TestMethod]
    public void MissingModernMetadataFallsBackWithoutInventingServicingRevision()
    {
        var legacy = WindowsVersionInfo.Create("Windows 10 Pro", null, "1809",
            "Client", 17763, null, new Version(10, 0, 17763, 0));
        Assert.AreEqual("1809", legacy.DisplayVersion);
        Assert.AreEqual("17763 (revision unavailable)", legacy.Build);
        var unavailable = WindowsVersionInfo.Create(null, null, null, null, 26300,
            null, new Version(10, 0, 26300, 0));
        Assert.AreEqual("Windows", unavailable.ProductName);
        Assert.AreEqual("version unavailable", unavailable.DisplayVersion);
    }
}
