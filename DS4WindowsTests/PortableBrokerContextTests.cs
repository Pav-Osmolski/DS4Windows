using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class PortableBrokerContextTests
{
    private string root, digest;
    private FakeHost host;
    private PortableBrokerContext context;

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(AppContext.BaseDirectory, "portable-broker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, PortableBrokerContext.MarkerFileName), PortableBrokerContext.MarkerText + "\n");
        byte[] image = "fake release image; never executed"u8.ToArray();
        File.WriteAllBytes(Path.Combine(root, "viiper.exe"), image);
        digest = Convert.ToHexString(SHA256.HashData(image));
        host = new FakeHost(Path.Combine(root, "viiper.exe"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        context?.Dispose();
        if (Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) &&
            Path.GetFileName(root).StartsWith("portable-broker-", StringComparison.Ordinal))
            Directory.Delete(root, recursive: true);
    }

    private PortableBrokerContext Open(Func<IEnumerable<string>> managed = null, Action<string> pathCheck = null) =>
        context = PortableBrokerContext.Create(root, digest, host, managed ?? (() => Array.Empty<string>()), pathCheck);

    [TestMethod]
    public void AbsentMarkerDoesNotInspectBrokerOrRegistryOrCreateData()
    {
        File.Delete(Path.Combine(root, PortableBrokerContext.MarkerFileName));
        host.SnapshotFailure = true;
        Assert.IsNull(Open(() => throw new InvalidOperationException("Registry must not be inspected.")));
        Assert.AreEqual(0, host.Snapshots);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "portable-data")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("DS4Windows portable package v2")]
    [DataRow(" DS4Windows portable package v1")]
    [DataRow("DS4Windows portable package v1 extra")]
    public void InvalidMarkerFailsClosed(string marker)
    {
        File.WriteAllText(Path.Combine(root, PortableBrokerContext.MarkerFileName), marker);
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => Open());
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    public void PreparationIsReadOnlyAndPinsExactReleaseImage()
    {
        Open();
        Assert.IsFalse(Directory.Exists(context.DataPath));
        Assert.IsTrue(context.IsVerifiedBackend(Path.Combine(root, "viiper.exe")));
        Assert.IsFalse(context.IsVerifiedBackend(Path.Combine(root, "elsewhere", "viiper.exe")));
        Assert.ThrowsExactly<IOException>(() => File.WriteAllText(context.ViiperPath, "replace"));
        Assert.IsTrue(context.InspectOwnedProcess(out bool running, out _));
        Assert.IsFalse(running);
        context.Dispose();
        Assert.IsFalse(context.IsVerifiedBackend(context.ViiperPath));
        File.WriteAllText(context.ViiperPath, "pin released");
    }

    [TestMethod]
    public void AdjacentSidecarCannotAuthorizeWrongBinary()
    {
        File.WriteAllText(Path.Combine(root, "viiper.exe.sha256"), digest);
        digest = new string('0', 64);
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => Open());
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "portable-data")));
        File.WriteAllText(Path.Combine(root, "viiper.exe"), "failed validation released handle");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RegisteredManagedRootAndDescendantsPreserveInstalledMode(bool parent)
    {
        host.SnapshotFailure = true;
        Assert.IsNull(Open(() => new[] { parent ? Path.GetDirectoryName(root) : root }));
        Assert.AreEqual(0, host.Snapshots);
        Assert.AreEqual(0, host.Starts);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "portable-data")));
    }

    [TestMethod]
    public void UpdaterCopiedStaleMarkerAndBadRootBrokerDoNotBrickManagedInstall()
    {
        File.WriteAllText(Path.Combine(root, PortableBrokerContext.MarkerFileName), "old or malformed portable marker");
        File.WriteAllText(Path.Combine(root, "viiper.exe"), "wrong or old root broker");
        host.SnapshotFailure = true;
        Assert.IsNull(Open(() => new[] { root + Path.DirectorySeparatorChar },
            _ => throw new IOException("Portable files must not be inspected in managed mode.")));
        Assert.AreEqual(0, host.Snapshots);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "portable-data")));
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow(@"C:relative")]
    [DataRow(@"C:\")]
    [DataRow(@"\\server\share\DS4Windows")]
    public void InvalidRegisteredPathCannotAuthorizeManagedBypass(string managed)
    {
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => Open(() => new[] { managed }));
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    public void NormalPortablePathPolicyIsNotTheLabStoragePolicy()
    {
        foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Assert.AreEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), PortableBrokerContext.ValidateRoot(path));
        }
        foreach (string path in new[] { ".", @"C:relative", @"\\server\share\portable", Path.GetPathRoot(root) })
            Assert.ThrowsExactly<ArgumentException>(() => PortableBrokerContext.ValidateRoot(path));
    }

    [TestMethod]
    [DataRow(Environment.SpecialFolder.ProgramFiles)]
    [DataRow(Environment.SpecialFolder.ProgramFilesX86)]
    [DataRow(Environment.SpecialFolder.Windows)]
    public void ProtectedFolderRejectionExplainsTheLocationAndHowToLaunch(Environment.SpecialFolder folder)
    {
        string systemPath = Environment.GetFolderPath(folder);
        if (string.IsNullOrEmpty(systemPath)) return;
        var error = Assert.ThrowsExactly<PortableBrokerStartupException>(() =>
            PortableBrokerContext.ValidateRoot(Path.Combine(systemPath, "DS4Windows")));
        string[] paragraphs = error.Message.Split("\n\n", StringSplitOptions.None);
        Assert.HasCount(2, paragraphs);
        StringAssert.Contains(paragraphs[0], "cannot run from Program Files");
        StringAssert.Contains(paragraphs[1], "Copy the entire portable folder");
        StringAssert.Contains(paragraphs[1], "DS4Windows installer");
    }

    [TestMethod]
    [DataRow("DS4Windows.portable")]
    [DataRow("viiper.exe")]
    [DataRow("portable-data/VIIPER")]
    [DataRow("portable-data/VIIPER/viiper.key.txt")]
    [DataRow("portable-data/VIIPER/viiper.json")]
    public void EverySelectedPathMustPassReparseInspection(string relative)
    {
        string rejected = Path.GetFullPath(Path.Combine(root, relative));
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => Open(pathCheck: path =>
        {
            if (path == rejected) throw new IOException("Injected link rejection.");
        }));
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    [DataRow("foreign")]
    [DataRow("unreadable")]
    [DataRow("multiple")]
    [DataRow("enumeration-failed")]
    public void ForeignMultipleOrUnknownOwnersAreNeverStoppedOrAdopted(string mode)
    {
        if (mode == "enumeration-failed") host.SnapshotFailure = true;
        else
        {
            host.Peers.Add(new(71, 72, mode == "unreadable" ? null : Path.Combine(root, "other", "viiper.exe")));
            if (mode == "multiple") host.Peers.Add(new(73, 74, host.ImagePath));
        }
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => Open());
        Assert.AreEqual(0, host.Starts);
        Assert.AreEqual(0, host.Child.StopCalls);
    }

    [TestMethod]
    public void LaunchUsesOnlyLocalExplicitConfigurationAndClearsInheritedOverrides()
    {
        string old = Environment.GetEnvironmentVariable("VIIPER_LOG_RAW_FILE");
        Environment.SetEnvironmentVariable("VIIPER_LOG_RAW_FILE", "must-not-be-opened");
        try
        {
            Open().Start();
            ProcessStartInfo start = host.StartInfo;
            Assert.AreEqual(context.ViiperPath, start.FileName);
            Assert.AreEqual(root, start.WorkingDirectory);
            Assert.IsFalse(start.UseShellExecute);
            Assert.IsTrue(start.CreateNoWindow);
            Assert.AreEqual(ProcessWindowStyle.Hidden, start.WindowStyle);
            Assert.IsTrue(string.IsNullOrEmpty(start.Verb));
            Assert.IsFalse(start.Environment.Keys.Any(key => key.StartsWith("VIIPER_", StringComparison.OrdinalIgnoreCase)));
            CollectionAssert.Contains(start.ArgumentList.ToArray(), "--config-only");
            CollectionAssert.Contains(start.ArgumentList.ToArray(), context.ConfigPath);
            CollectionAssert.Contains(start.ArgumentList.ToArray(), context.KeyPath);
            CollectionAssert.Contains(start.ArgumentList.ToArray(), "--api.require-local-host-auth=true");
            CollectionAssert.Contains(start.ArgumentList.ToArray(), "--usb.retained-import-authority-id=4923336367393615921");
            CollectionAssert.Contains(start.ArgumentList.ToArray(), "--api.addr=127.0.0.1:3242");
            CollectionAssert.Contains(start.ArgumentList.ToArray(), "--usb.addr=127.0.0.1:3241");
            Assert.AreEqual("{}", File.ReadAllText(context.ConfigPath));
            using (var ordinaryReader = new FileStream(context.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.AreEqual((int)'{', ordinaryReader.ReadByte());
            Assert.ThrowsExactly<IOException>(() => File.WriteAllText(context.ConfigPath, "replacement"));
            Assert.ThrowsExactly<IOException>(() => File.Delete(context.ConfigPath));
            Assert.IsFalse(File.Exists(context.KeyPath), "Only VIIPER creates the real key; this fake host never runs VIIPER.");
            context.Start();
            Assert.AreEqual(1, host.Starts);
            Assert.IsTrue(context.InspectOwnedProcess(out bool running, out _));
            Assert.IsTrue(running);
        }
        finally { Environment.SetEnvironmentVariable("VIIPER_LOG_RAW_FILE", old); }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TrayPreferenceAppliesOnNextPortableStartWithoutRestartingCurrentOwner(bool visible)
    {
        bool previous = Global.ShowViiperTrayIcon;
        try
        {
            Global.ShowViiperTrayIcon = visible;
            Open().Start();
            CollectionAssert.Contains(host.StartInfo.ArgumentList.ToArray(),
                visible ? "--no-tray=false" : "--no-tray=true");
            Global.ShowViiperTrayIcon = !visible;
            Assert.IsTrue(context.InspectOwnedProcess(out bool running, out _));
            Assert.IsTrue(running);
            Assert.AreEqual(1, host.Starts);
            Assert.AreEqual(0, host.Child.StopCalls);
        }
        finally { Global.ShowViiperTrayIcon = previous; }
    }

    [TestMethod]
    public void ExistingChangedConfigIsNotOverwrittenAndNoChildStarts()
    {
        Open();
        Directory.CreateDirectory(context.DataPath);
        File.WriteAllText(context.ConfigPath, "{\"log.file\":\"elsewhere\"}");
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual("{\"log.file\":\"elsewhere\"}", File.ReadAllText(context.ConfigPath));
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    public void ExistingEmptyConfigIsNotSilentlyOverwritten()
    {
        Open();
        Directory.CreateDirectory(context.DataPath);
        File.WriteAllText(context.ConfigPath, "");
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual(0L, new FileInfo(context.ConfigPath).Length);
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    public void ConfigurationChangedBetweenCreationAndReadPinCannotLaunch()
    {
        int configInspections = 0;
        string configPath = Path.Combine(root, "portable-data", "VIIPER", "viiper.json");
        Open(pathCheck: path =>
        {
            PortableLabContext.ValidateNoReparsePoints(path);
            if (path == configPath && ++configInspections == 4)
                File.WriteAllText(configPath, "{\"changed\":true}");
        });
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual("{\"changed\":true}", File.ReadAllText(configPath));
        Assert.AreEqual(0, host.Starts);
    }

    [TestMethod]
    public void ProcessAppearingAfterPreparationPreventsLaunch()
    {
        Open();
        host.Peers.Add(new(99, 100, Path.Combine(root, "other", "viiper.exe")));
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual(0, host.Starts);
        Assert.AreEqual(0, host.Child.StopCalls);
    }

    [TestMethod]
    public void ForeignLaunchRaceStopsOnlyOurNewChild()
    {
        Open();
        host.AfterStart = () => host.Peers.Add(new(99, 100, Path.Combine(root, "other", "viiper.exe")));
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual(1, host.Child.StopCalls);
        Assert.AreEqual(1, host.Peers.Count);
        Assert.AreEqual(99, host.Peers[0].ProcessId);
    }

    [TestMethod]
    public void FailedLaunchCannotFallBackOrRepeat()
    {
        Open();
        host.StartFailure = true;
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual(1, host.Starts);
    }

    [TestMethod]
    [DataRow(70, "could not be found")]
    [DataRow(71, "does not match")]
    [DataRow(72, "timed out")]
    [DataRow(73, "compatibility check")]
    [DataRow(74, "connection key")]
    [DataRow(75, "USB/IP listener")]
    [DataRow(76, "API listener")]
    public void EarlyBrokerExitPreservesItsStartupCheckInTheFailure(int code, string expected)
    {
        Open();
        host.AfterStart = () =>
        {
            host.Child.Running = false;
            host.Child.StartupExitCode = code;
            host.Peers.Clear();
        };
        var failure = Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        StringAssert.Contains(failure.Message, expected);
        Assert.AreEqual(0, host.Child.StopCalls, "An exited process needs no termination.");
        Assert.AreEqual(1, host.Child.DisposeCalls);
    }

    [TestMethod]
    public void ExitDuringReadinessPollingPreservesTheStartupCheck()
    {
        Open().Start();
        host.Child.Running = false;
        host.Child.StartupExitCode = 72;
        host.Peers.Clear();
        Assert.IsFalse(context.InspectOwnedProcess(out bool running, out string failure));
        Assert.IsFalse(running);
        StringAssert.Contains(failure, "USB/IP startup check timed out");
    }

    [TestMethod]
    public void PortSelectionFallsBackWhenOneOfThePreferredPortsCannotBind()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        occupied.Start();
        int occupiedPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var preferred = new PortableBrokerPorts(occupiedPort, 3242);
        PortableBrokerPorts selected = PortableBrokerPorts.Select(useFallback: false, preferred);
        Assert.AreNotEqual(occupiedPort, selected.Usb);
        Assert.AreNotEqual(occupiedPort, selected.Api);
        Assert.AreNotEqual(selected.Usb, selected.Api);
        using var usb = new TcpListener(IPAddress.Loopback, selected.Usb) { ExclusiveAddressUse = true };
        using var api = new TcpListener(IPAddress.Loopback, selected.Api) { ExclusiveAddressUse = true };
        usb.Start();
        api.Start();
    }

    [TestMethod]
    [DataRow(75, false)]
    [DataRow(76, true)]
    public void ListenerFailureRetriesOnceAndAuthenticatesOnTheNewApiPort(int exitCode, bool duringProbe)
    {
        var alternate = new PortableBrokerPorts(55001, 55002);
        host.FallbackPorts = alternate;
        Open();
        if (!duringProbe) host.AfterStart = () =>
        {
            if (host.Starts == 1) ExitChild();
        };
        context.StartAndWaitUntilReady(probe: Probe);

        Assert.AreEqual(2, host.Starts);
        Assert.AreEqual(alternate, context.Ports);
        CollectionAssert.Contains(host.StartInfo.ArgumentList.ToArray(), "--usb.addr=127.0.0.1:55001");
        Assert.AreEqual(1, host.Child.DisposeCalls, "The exited child must be retired before retrying.");
        Assert.AreEqual(0, host.Child.StopCalls);

        bool Probe(int port, int timeout, out string failure)
        {
            failure = null;
            if (duringProbe && host.Starts == 1)
            {
                Assert.AreEqual(ViiperSetupManager.ApiPort, port);
                ExitChild();
                return false;
            }
            Assert.AreEqual(alternate.Api, port);
            return true;
        }

        void ExitChild()
        {
            host.Child.Running = false;
            host.Child.StartupExitCode = exitCode;
            host.Peers.Clear();
        }
    }

    [TestMethod]
    [DataRow(74, 1)]
    [DataRow(75, 2)]
    public void StartupDoesNotRetryKeyFailuresOrKeepRetryingUnavailableListeners(int exitCode, int attempts)
    {
        Open();
        host.AfterStart = () =>
        {
            host.Child.Running = false;
            host.Child.StartupExitCode = exitCode;
            host.Peers.Clear();
        };
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.StartAndWaitUntilReady());
        Assert.AreEqual(attempts, host.Starts);
    }

    [TestMethod]
    public void OptInPortableBrokerAndControllerWorkWithBothDefaultPortsBlocked()
    {
        string image = Environment.GetEnvironmentVariable("DS4W_PORTABLE_VIIPER_PORT_SMOKE");
        if (string.IsNullOrEmpty(image))
            Assert.Inconclusive("Set DS4W_PORTABLE_VIIPER_PORT_SMOKE to the bundled VIIPER executable on a test host with USB/IP installed.");
        if (Process.GetProcessesByName("viiper").Length != 0 || Process.GetProcessesByName("DS4Windows").Length != 0)
            Assert.Inconclusive("Close DS4Windows and VIIPER before running the isolated portable smoke test.");

        File.Copy(image, host.ImagePath, overwrite: true);
        using var blockedUsb = BlockIfAvailable(PortableBrokerPorts.DefaultUsbPort);
        using var blockedApi = BlockIfAvailable(ViiperSetupManager.ApiPort);
        bool tray = Global.ShowViiperTrayIcon;
        bool debug = Global.ViiperDebugLogging;
        try
        {
            Global.ShowViiperTrayIcon = false;
            Global.ViiperDebugLogging = false;
            PortableBrokerContext.Initialize(root);
            PortableBrokerContext.Current.StartAndWaitUntilReady();
            PortableBrokerPorts selected = PortableBrokerPorts.Current;
            Assert.AreNotEqual(PortableBrokerPorts.DefaultUsbPort, selected.Usb);
            Assert.AreNotEqual(ViiperSetupManager.ApiPort, selected.Api);
            Assert.IsTrue(ViiperSetupManager.GetFreshDependencyStatus().Ready);
            using (var device = new ViiperClient(ViiperSetupManager.ApiHost, selected.Api)
                .CreateDeviceAndOpenStream(ViiperVirtualDeviceType.Xbox360))
            {
                Assert.IsTrue(device.UsbipPort > 0);
                Assert.IsFalse(ViiperUsbipPortManager.CanUseUsbipPortAsInput(device.UsbipPort));
            }

            PortableBrokerContext.RetireCurrentForRepair();
            PortableBrokerContext.Initialize(root);
            PortableBrokerContext.Current.StartAndWaitUntilReady(selected);
            Assert.AreEqual(selected, PortableBrokerPorts.Current);
            Assert.IsTrue(ViiperSetupManager.GetFreshDependencyStatus().Ready);
        }
        finally
        {
            try { PortableBrokerContext.RetireCurrentForRepair(); }
            finally
            {
                Global.ShowViiperTrayIcon = tray;
                Global.ViiperDebugLogging = debug;
            }
        }

        static TcpListener BlockIfAvailable(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            try
            {
                listener.Start();
                return listener;
            }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.AccessDenied or SocketError.AddressAlreadyInUse)
            {
                listener.Dispose();
                return null;
            }
        }
    }

    [TestMethod]
    public void SlowReadinessPreservesOwnedProcessAndTimedOutAttemptCanBeRecreated()
    {
        Open().Start();
        long now = 0;
        bool Probe(int timeout, out string failure)
        {
            Assert.IsTrue(context.InspectOwnedProcess(out bool running, out failure));
            Assert.IsTrue(running);
            if (now >= 20_100) return true;
            now += timeout;
            return false;
        }
        Assert.IsTrue(ViiperStartupReadiness.Wait(Probe, out _, () => now, ms => now += ms));
        Assert.AreEqual(0, host.Child.StopCalls, "Prerequisite startup must not be mistaken for a dead child.");
        context.Dispose();
        Assert.AreEqual(1, host.Child.StopCalls);

        Open().Start();
        now = 0;
        bool NeverReady(int timeout, out string failure)
        {
            now += timeout;
            failure = "Connect: SocketException";
            return false;
        }
        Assert.IsFalse(ViiperStartupReadiness.Wait(NeverReady, out _, () => now, ms => now += ms));
        context.Dispose();
        Assert.AreEqual(2, host.Child.StopCalls);
        Assert.AreEqual(0, host.Peers.Count);
        Open().Start();
        Assert.IsTrue(context.InspectOwnedProcess(out bool restarted, out _));
        Assert.IsTrue(restarted, "A failed attempt must not require a machine reboot to recreate its owner.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CleanupTerminatesOnlyTheStillIdenticalOwnedChild(bool sameIdentity)
    {
        Open().Start();
        host.Child.SameIdentity = sameIdentity;
        context.Dispose();
        context.Dispose();
        Assert.AreEqual(sameIdentity ? 1 : 0, host.Child.StopCalls);
        Assert.AreEqual(1, host.Child.DisposeCalls);
        if (sameIdentity) Assert.AreEqual(5000, host.Child.StopTimeout);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DebugCaptureStartsWithManagedAuthenticationAndToggleDoesNotRestart(bool borrow)
    {
        bool previous = Global.ViiperDebugLogging;
        string previousData = Global.appdatapath;
        try
        {
            Global.ViiperDebugLogging = true;
            Global.appdatapath = root;
            if (borrow)
            {
                PrepareBorrowed();
                File.WriteAllText(ViiperDiagnostics.LogPath, "borrowed capture must survive");
                context.Start();
                Assert.AreEqual("borrowed capture must survive", File.ReadAllText(ViiperDiagnostics.LogPath));
                Assert.AreEqual(0, host.Starts);
            }
            else
            {
                Open().Start();
                CollectionAssert.Contains(host.StartInfo.ArgumentList.ToArray(), "--log.level=debug");
                CollectionAssert.Contains(host.StartInfo.ArgumentList.ToArray(), "--log.file=" + ViiperDiagnostics.LogPath);
                CollectionAssert.Contains(host.StartInfo.ArgumentList.ToArray(), "--key-file");
                CollectionAssert.Contains(host.StartInfo.ArgumentList.ToArray(), "--api.require-local-host-auth=true");
                Assert.AreEqual(1, host.Starts);
            }
            Global.ViiperDebugLogging = false;
            Assert.IsTrue(context.InspectOwnedProcess(out bool running, out _));
            Assert.IsTrue(running);
            context.Start();
            Assert.AreEqual(0, host.Child.StopCalls);
        }
        finally
        {
            Global.ViiperDebugLogging = previous;
            Global.appdatapath = previousData;
        }
    }

    [TestMethod]
    public void DebugLoggingDoesNotAuthorizeChangedAuthenticationOrArbitraryArguments()
    {
        bool previous = Global.ViiperDebugLogging;
        string previousData = Global.appdatapath;
        try
        {
            Global.ViiperDebugLogging = true;
            Global.appdatapath = root;
            PrepareBorrowed();
            host.Arguments = host.Arguments.Where(a => a != "--api.require-local-host-auth=true").ToArray();
            Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
            Assert.AreEqual(0, host.Starts);
            Assert.AreEqual(0, host.Child.StopCalls);
        }
        finally
        {
            Global.ViiperDebugLogging = previous;
            Global.appdatapath = previousData;
        }
    }

    private void PrepareBorrowed()
    {
        Open().Start();
        string[] arguments = host.StartInfo.ArgumentList.ToArray();
        string keyPath = context.KeyPath;
        context.Dispose();
        File.WriteAllText(keyPath, "public fake key; never authenticates a real broker");
        host = new FakeHost(Path.Combine(root, "viiper.exe")) { Arguments = arguments };
        host.Peers.Add(new(501, 502, host.ImagePath));
        Open();
    }

    [TestMethod]
    public void ExactExistingPortableLaunchCanBeBorrowedButNeverStopped()
    {
        PrepareBorrowed();
        context.Start();
        Assert.AreEqual(0, host.Starts);
        Assert.AreEqual(1, host.ArgumentReads);
        Assert.IsTrue(context.InspectOwnedProcess(out bool running, out _));
        Assert.IsTrue(running);
        context.Dispose();
        Assert.AreEqual(0, host.Child.StopCalls);
        Assert.AreEqual(1, host.Peers.Count);
    }

    [TestMethod]
    public void BorrowedPortableLaunchKeepsItsVerifiedAlternatePorts()
    {
        PrepareBorrowed();
        host.Arguments = host.Arguments.Select(value => value switch
        {
            "--usb.addr=127.0.0.1:3241" => "--usb.addr=127.0.0.1:55001",
            "--api.addr=127.0.0.1:3242" => "--api.addr=127.0.0.1:55002",
            _ => value,
        }).ToArray();
        context.StartAndWaitUntilReady(probe: Probe);
        Assert.AreEqual(new PortableBrokerPorts(55001, 55002), context.Ports);
        Assert.AreEqual(0, host.Starts);

        static bool Probe(int port, int timeout, out string failure)
        {
            Assert.AreEqual(55002, port);
            failure = null;
            return true;
        }
    }

    [TestMethod]
    [DataRow("key")]
    [DataRow("config-only")]
    [DataRow("authority")]
    [DataRow("unreadable")]
    [DataRow("pid-reused")]
    [DataRow("non-loopback")]
    [DataRow("same-port")]
    public void SameImageDoesNotAuthorizeUnknownOrChangedExistingLaunch(string change)
    {
        PrepareBorrowed();
        if (change == "key") File.WriteAllText(context.KeyPath, " ");
        if (change == "config-only") host.Arguments = host.Arguments.Where(value => value != "--config-only").ToArray();
        if (change == "authority") host.Arguments = host.Arguments.Where(value => !value.StartsWith("--usb.retained-import-authority-id=")).ToArray();
        if (change == "unreadable") host.ArgumentFailure = true;
        if (change == "pid-reused") host.AfterReadArguments = () => host.Peers[0] = host.Peers[0] with { StartTimeUtcTicks = 999 };
        if (change == "non-loopback") host.Arguments = host.Arguments.Select(value =>
            value == "--api.addr=127.0.0.1:3242" ? "--api.addr=0.0.0.0:3242" : value).ToArray();
        if (change == "same-port") host.Arguments = host.Arguments.Select(value =>
            value == "--api.addr=127.0.0.1:3242" ? "--api.addr=127.0.0.1:3241" : value).ToArray();
        Assert.ThrowsExactly<PortableBrokerStartupException>(() => context.Start());
        Assert.AreEqual(0, host.Starts);
        Assert.AreEqual(0, host.Child.StopCalls);
        Assert.AreEqual(1, host.Peers.Count);
    }

    private sealed class FakeHost : IPortableBrokerProcessHost
    {
        internal readonly string ImagePath;
        internal readonly List<PortableBrokerProcessIdentity> Peers = new();
        internal readonly FakeProcess Child;
        internal int Snapshots, Starts, ArgumentReads;
        internal bool SnapshotFailure, StartFailure, ArgumentFailure;
        internal Action AfterStart, AfterReadArguments;
        internal ProcessStartInfo StartInfo;
        internal string[] Arguments;
        internal PortableBrokerPorts FallbackPorts = new(55001, 55002);
        internal FakeHost(string path) { ImagePath = path; Child = new FakeProcess(this); }
        public IReadOnlyList<PortableBrokerProcessIdentity> Snapshot()
        {
            Snapshots++;
            if (SnapshotFailure) throw new IOException("Unreadable owner");
            return Peers.ToArray();
        }
        public IReadOnlyList<string> ReadArguments(PortableBrokerProcessIdentity identity)
        {
            ArgumentReads++;
            if (ArgumentFailure) throw new IOException("Unreadable arguments");
            AfterReadArguments?.Invoke();
            return Arguments;
        }
        public IPortableBrokerProcess Start(ProcessStartInfo startInfo)
        {
            Starts++;
            if (StartFailure) throw new IOException("Injected launch failure");
            StartInfo = startInfo;
            Child.Running = true;
            Peers.Add(new(Child.ProcessId, Child.StartTimeUtcTicks, ImagePath));
            AfterStart?.Invoke();
            return Child;
        }
        public PortableBrokerPorts SelectPorts(bool useFallback, PortableBrokerPorts preferred) =>
            useFallback ? FallbackPorts : preferred ?? PortableBrokerPorts.Default;
    }

    private sealed class FakeProcess : IPortableBrokerProcess
    {
        private readonly FakeHost host;
        internal bool Running, SameIdentity = true;
        internal int? StartupExitCode;
        internal int StopCalls, DisposeCalls, StopTimeout;
        internal FakeProcess(FakeHost host) { this.host = host; }
        public int ProcessId => 123;
        public long StartTimeUtcTicks => 456;
        public bool IsRunning => Running;
        public bool IdentityMatches => SameIdentity;
        public int? ExitCode => StartupExitCode;
        public void StopAndWait(int timeoutMilliseconds)
        {
            StopCalls++;
            StopTimeout = timeoutMilliseconds;
            Running = false;
            host.Peers.RemoveAll(peer => peer.ProcessId == ProcessId && peer.StartTimeUtcTicks == StartTimeUtcTicks);
        }
        public void Dispose() => DisposeCalls++;
    }
}
