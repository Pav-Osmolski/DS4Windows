using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DS4Windows.Installation;

namespace DS4WindowsTests;

[TestClass]
public sealed class InstallerStartupTaskPolicyTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string Exe = @"C:\Program Files\DS4Windows\DS4Windows.exe";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OnlyMarkedExactManagedActionsAreEligible(bool viiper)
    {
        string path = viiper ? @"C:\Program Files\DS4Windows\VIIPER\viiper.exe" : Exe;
        string arguments = viiper ? InstallerStartupTaskPolicy.ViiperArguments : "-m";
        var xml = Definition(path, arguments);
        Assert.IsTrue(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), path, arguments, Sid, true));
        xml.Descendants(Ns + "Description").Single().Value = "";
        Assert.IsFalse(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), path, arguments),
            "An unrepaired, unmarked same-path task must not be trusted by launch or uninstall.");
    }

    [TestMethod]
    [DataRow("Description", "User task")]
    [DataRow("Command", @"C:\Foreign\DS4Windows.exe")]
    [DataRow("Command", "DS4Windows.exe")]
    [DataRow("Arguments", "-m --unexpected")]
    [DataRow("WorkingDirectory", @"C:\Foreign")]
    [DataRow("UserId", "S-1-5-21-999-999-999-1001")]
    [DataRow("LogonType", "Password")]
    [DataRow("RunLevel", "LeastPrivilege")]
    public void UnrecognizedOrOtherUserDefinitionIsNotLaunched(string field, string value)
    {
        var xml = Definition();
        xml.Descendants(Ns + field).Single().Value = value;
        Assert.IsFalse(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), Exe, "-m", Sid, true));
    }

    [TestMethod]
    [DataRow("Actions")]
    [DataRow("Triggers")]
    [DataRow("Principals")]
    public void ExtraDefinitionEntriesDoNotProveOwnership(string container)
    {
        var xml = Definition();
        XElement list = xml.Element(Ns + container);
        list.Add(new XElement(list.Elements().Single()));
        Assert.IsFalse(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), Exe, "-m"));
    }

    [TestMethod]
    public void DisabledOwnedTasksCanBeRemovedButNeverLaunched()
    {
        var xml = Definition();
        xml.Add(new XElement(Ns + "Settings", new XElement(Ns + "Enabled", false)));
        Assert.IsTrue(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), Exe, "-m"));
        Assert.IsFalse(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), Exe, "-m", Sid, true));
        xml.Element(Ns + "Settings").Remove();
        xml.Descendants(Ns + "LogonTrigger").Single().Add(new XElement(Ns + "Enabled", false));
        Assert.IsTrue(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), Exe, "-m"));
        Assert.IsFalse(InstallerStartupTaskPolicy.IsManaged(xml.ToString(), Exe, "-m", Sid, true));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<Task/>")]
    [DataRow("<!DOCTYPE x [<!ENTITY injected 'value'>]><Task>&injected;</Task>")]
    public void InvalidXmlIsNotOwnership(string xml) =>
        Assert.IsFalse(InstallerStartupTaskPolicy.IsManaged(xml, Exe, "-m"));

    [TestMethod]
    public void OnlyCurrentInvocationStartupWarningsAppearOnFinish()
    {
        const string current = "aaaabbbbccccddddeeeeffff11112222";
        Assert.IsTrue(InstallerStartupTaskPolicy.HasCurrentWarning(current, current.ToUpperInvariant(), "denied"));
        Assert.IsFalse(InstallerStartupTaskPolicy.HasCurrentWarning(current, "bbbbbbbbccccddddeeeeffff11112222", "old"));
        Assert.IsFalse(InstallerStartupTaskPolicy.HasCurrentWarning(current, current, ""));
        Assert.IsFalse(InstallerStartupTaskPolicy.HasCurrentWarning(null, null, "denied"));
        Assert.IsFalse(InstallerStartupTaskPolicy.HasCurrentWarning("bad", "bad", "denied"));
    }

    [TestMethod]
    public void InstallerConsumersUseOriginalNamesAndOwnershipGuards()
    {
        string root = Path.Combine(Path.GetDirectoryName(SourcePath()), "..", "installer");
        string setup = File.ReadAllText(Path.Combine(root, "DS4Windows.SetupActions", "Program.cs"));
        StringAssert.Contains(setup, "RemoveOwnedTask(\"RunVIIPER\",");
        StringAssert.Contains(setup, "RemoveOwnedTask(\"RunDS4Windows\",");
        StringAssert.Contains(setup, "InstallerStartupTaskPolicy.IsManaged(output,");
        string bootstrap = File.ReadAllText(Path.Combine(root, "DS4Windows.Bootstrapper", "InstallerApplication.cs"));
        Assert.IsFalse(bootstrap.Contains("DS4Windows.RunDS4Windows"));
        StringAssert.Contains(bootstrap, "InstallerStartupTaskPolicy.IsManaged(xml,");
        StringAssert.Contains(bootstrap, "running != null && WaitForInstalledDs4Process(executable)");
        Assert.IsFalse(bootstrap.Contains("/Run /TN \\\"RunDS4Windows\\\""),
            "Finish must not blindly launch a colliding legacy task.");
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;

    private static XElement Definition(string path = Exe, string arguments = "-m") => new(Ns + "Task",
        new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Description", InstallerStartupTaskPolicy.ManagedDescription)),
        new XElement(Ns + "Triggers", new XElement(Ns + "LogonTrigger")),
        new XElement(Ns + "Principals", new XElement(Ns + "Principal",
            new XElement(Ns + "UserId", Sid), new XElement(Ns + "LogonType", "InteractiveToken"),
            new XElement(Ns + "RunLevel", "HighestAvailable"))),
        new XElement(Ns + "Actions", new XElement(Ns + "Exec", new XElement(Ns + "Command", path),
            new XElement(Ns + "Arguments", arguments), new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(path)))));
}
