using DS4Windows.Bootstrapper;
using WixToolset.BootstrapperApplicationApi;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DS4WindowsTests;

[TestClass]
public class OptionalDriverSelectionTests
{
    private static Dictionary<string, PackageState> Packages(PackageState hidHide, PackageState fakerInput) =>
        new() { ["HidHide"] = hidHide, ["FakerInput"] = fakerInput };

    [TestMethod]
    public void FreshInstallRecommendsOnlyMissingHidHide()
    {
        var selection = new OptionalDriverSelection();
        selection.Detect(Packages(PackageState.Absent, PackageState.Absent), false);
        Assert.IsTrue(selection.InstallHidHide);
        Assert.IsFalse(selection.InstallFakerInput, "FakerInput remains opt-in.");
        Assert.AreEqual("Not installed — will install", OptionalDriverSelection.Status(selection.HidHide, selection.InstallHidHide));
        Assert.AreEqual("Not installed", OptionalDriverSelection.Status(selection.FakerInput, selection.InstallFakerInput));
    }

    [TestMethod]
    [DataRow(PackageState.Present)]
    [DataRow(PackageState.Superseded)]
    public void ExistingDriversAreUntickedAndPreserved(PackageState state)
    {
        var selection = new OptionalDriverSelection();
        selection.Detect(Packages(state, state), false);
        Assert.IsFalse(selection.InstallHidHide); Assert.IsFalse(selection.InstallFakerInput);
        Assert.AreEqual(OptionalDriverPresence.Installed, selection.HidHide);
        Assert.AreEqual(OptionalDriverPresence.Installed, selection.FakerInput);
        Assert.AreEqual("Installed — kept unchanged", OptionalDriverSelection.Status(selection.HidHide, false));
    }

    [TestMethod]
    [DataRow(PackageState.Unknown)]
    [DataRow(PackageState.Obsolete)]
    public void UnknownOrObsoleteStateNeverCausesAutomaticInstallation(PackageState state)
    {
        var selection = new OptionalDriverSelection(); selection.Detect(Packages(state, state), false);
        Assert.AreEqual(OptionalDriverPresence.Unknown, selection.HidHide);
        Assert.IsFalse(selection.InstallHidHide); Assert.IsFalse(selection.InstallFakerInput);
    }

    [TestMethod]
    public void MissingDetectionAndRelatedFakerInputAreHandledConservatively()
    {
        var selection = new OptionalDriverSelection();
        selection.Detect(new Dictionary<string, PackageState>(), true);
        Assert.AreEqual(OptionalDriverPresence.Unknown, selection.HidHide);
        Assert.IsFalse(selection.InstallHidHide);
        Assert.AreEqual(OptionalDriverPresence.Installed, selection.FakerInput);
        Assert.IsFalse(selection.InstallFakerInput);
    }

    [TestMethod]
    public void RedetectionPreservesUserChoicesAndClearsChoicesForNowInstalledDrivers()
    {
        var selection = new OptionalDriverSelection();
        selection.Detect(Packages(PackageState.Absent, PackageState.Absent), false);
        selection.InstallHidHide = false; selection.InstallFakerInput = true;
        selection.Detect(Packages(PackageState.Absent, PackageState.Absent), false);
        Assert.IsFalse(selection.InstallHidHide); Assert.IsTrue(selection.InstallFakerInput);
        selection.Detect(Packages(PackageState.Present, PackageState.Present), false);
        Assert.IsFalse(selection.InstallHidHide); Assert.IsFalse(selection.InstallFakerInput);
    }

    [TestMethod]
    public void UninstallNeverAdvertisesOptionalDriverInstallation()
    {
        Assert.AreEqual("Not installed", OptionalDriverSelection.Status(OptionalDriverPresence.Missing, true, true));
        Assert.AreEqual("Installed — kept unchanged", OptionalDriverSelection.Status(OptionalDriverPresence.Installed, false, true));
    }

    [TestMethod]
    [DataRow(LaunchAction.Install)]
    [DataRow(LaunchAction.Repair)]
    public void ExistingOptionalDriversAreExcludedFromForwardMaintenance(LaunchAction action)
    {
        var installed = Packages(PackageState.Present, PackageState.Present);
        Assert.IsTrue(OptionalDriverSelection.PreserveExisting(action, "HidHide", installed, false));
        Assert.IsTrue(OptionalDriverSelection.PreserveExisting(action, "FakerInput", installed, false));
        Assert.IsTrue(OptionalDriverSelection.PreserveExisting(action, "FakerInput", Packages(PackageState.Absent, PackageState.Obsolete), true));
        Assert.IsFalse(OptionalDriverSelection.PreserveExisting(action, "HidHide", Packages(PackageState.Absent, PackageState.Absent), false));
        Assert.IsFalse(OptionalDriverSelection.PreserveExisting(action, "DS4WindowsMsi", installed, false));
        Assert.IsFalse(OptionalDriverSelection.PreserveExisting(action, "ViiperUsbipSetup", installed, false));
        Assert.IsFalse(OptionalDriverSelection.PreserveExisting(LaunchAction.Uninstall, "HidHide", installed, false));
    }

    [TestMethod]
    public void BundleRecognizesHidHideAcrossProductVersionsAndStatusListsBothDrivers()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source()), ".."));
        var bundle = XDocument.Load(Path.Combine(root, "installer/DS4Windows.Bundle/Bundle.wxs"));
        XNamespace util = "http://wixtoolset.org/schemas/v4/wxs/util";
        var search = bundle.Descendants(util + "RegistrySearch").Single(e => (string)e.Attribute("Id") == "HidHideDriverSearch");
        Assert.AreEqual(@"SYSTEM\CurrentControlSet\Services\HidHide", (string)search.Attribute("Key"));
        Assert.AreEqual("always64", (string)search.Attribute("Bitness"));
        var hidHide = bundle.Descendants().Single(e => e.Name.LocalName == "ExePackage" && (string)e.Attribute("Id") == "HidHide");
        Assert.AreEqual("HidHideInstalled OR HidHideDriverInstalled", (string)hidHide.Attribute("DetectCondition"));
        var xaml = XDocument.Load(Path.Combine(root, "installer/DS4Windows.Bootstrapper/InstallerWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string name in new[] { "HidHideStatus", "FakerInputStatus", "ViiperLabel" })
            Assert.IsTrue(xaml.Descendants().Any(e => (string)e.Attribute(x + "Name") == name));
        Assert.IsFalse(xaml.ToString().Contains("VIIPER 0.1.10"));
        Assert.AreEqual("False", (string)xaml.Descendants().Single(e => (string)e.Attribute(x + "Name") == "HidHideCheckBox").Attribute("IsChecked"));
    }
    private static string Source([CallerFilePath] string path = "") => path;
}
