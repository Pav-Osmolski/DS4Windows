using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4Windows.Tests;

[TestClass]
public class ViiperSystemDriverIntegrityTests
{
    [TestMethod]
    public void HashesRegisteredFileAndObservesReplacementOnNextInspection()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sys");
        try
        {
            byte[] original = { 1, 2, 3 };
            File.WriteAllBytes(path, original);
            string ReadPath(string service)
            {
                Assert.AreEqual("usbip2_ude", service);
                return "\"" + path + "\"";
            }

            Assert.IsTrue(ViiperSetupManager.TryGetSystemDriverSha256(
                "usbip2_ude", out string first, out string error, ReadPath));
            Assert.IsNull(error);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(original)), first);

            byte[] replacement = { 4, 5, 6 };
            File.WriteAllBytes(path, replacement);
            Assert.IsTrue(ViiperSetupManager.TryGetSystemDriverSha256(
                "usbip2_ude", out string second, out error, ReadPath));
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(replacement)), second);
            Assert.AreNotEqual(first, second);
            Assert.IsFalse(ViiperSetupManager.AreSupportedUsbipDriverHashes(first, second));
        }
        finally { File.Delete(path); }
    }

    [DataTestMethod]
    [DataRow(null, "service is missing")]
    [DataRow("", "driver file is missing")]
    [DataRow("\"", "driver file is missing")]
    public void MissingRegistrationOrImageNeverAuthorizesStartup(string imagePath, string message)
    {
        Assert.IsFalse(ViiperSetupManager.TryGetSystemDriverSha256(
            "usbip2_filter", out string hash, out string error, _ => imagePath));
        Assert.IsNull(hash);
        StringAssert.Contains(error, message);
    }

    [TestMethod]
    public void MissingRegisteredFileNeverFallsBackToAnotherDriver()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sys");
        Assert.IsFalse(ViiperSetupManager.TryGetSystemDriverSha256(
            "usbip2_filter", out string hash, out string error, _ => missing));
        Assert.IsNull(hash);
        StringAssert.Contains(error, "driver file is missing");
    }

    [TestMethod]
    public void UnreadableRegistrationPropagatesToIntegrityFailureHandler()
    {
        Assert.ThrowsException<UnauthorizedAccessException>(() =>
            ViiperSetupManager.TryGetSystemDriverSha256("usbip2_ude", out _, out _,
                _ => throw new UnauthorizedAccessException("Denied")));
    }

    [DataTestMethod]
    [DataRow(@"\SystemRoot\System32\drivers\usbip2_ude.sys")]
    [DataRow(@"System32\drivers\usbip2_ude.sys")]
    [DataRow(@"%SystemRoot%\System32\drivers\usbip2_ude.sys")]
    public void ResolvesRegistryDriverPathForms(string imagePath)
    {
        string expected = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"),
            @"System32\drivers\usbip2_ude.sys");
        Assert.AreEqual(expected, ViiperSetupManager.ResolveSystemDriverPath(imagePath), true);
        Assert.AreEqual(expected, ViiperSetupManager.ResolveSystemDriverPath(@"\??\" + expected), true);
        Assert.AreEqual(expected, ViiperSetupManager.ResolveSystemDriverPath(@"\\?\" + expected), true);
    }
}
