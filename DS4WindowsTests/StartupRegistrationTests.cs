using DS4WinWPF;
using DS4WinWPF.DS4Forms.ViewModels;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class StartupRegistrationTests
{
    [TestMethod]
    [DataRow(false, false, false, false, false)]
    [DataRow(true, true, true, true, false)]
    [DataRow(true, true, true, false, true)]
    [DataRow(true, false, true, false, false)]
    [DataRow(true, true, false, false, false)]
    [DataRow(true, false, false, false, false)]
    public void AutomaticRepairNeverEnablesDisabledOrForeignTasks(
        bool exists, bool enabled, bool owned, bool exact, bool expected)
    {
        Assert.AreEqual(expected, StartupRegistrationPolicy.ShouldRepairTask(
            exists, enabled, owned, exact));
    }

    [TestMethod]
    public void PassiveRepairCannotOverrideAnExplicitDisableAfterFailedRemoval()
    {
        Assert.IsFalse(StartupRegistrationPolicy.ShouldRepairTask(
            exists: true, enabled: true, owned: true,
            matchesCurrentConfiguration: false, requested: false));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PassiveRepairCannotBypassDeferredSetupEvenWhenStartupIsRequested(
        bool actualEnabled)
    {
        Assert.IsFalse(StartupRegistrationPolicy.ShouldRepairTask(
            exists: true, enabled: actualEnabled, owned: true,
            matchesCurrentConfiguration: false, requested: true, setupDeferred: true));
    }

    [TestMethod]
    public void ExplicitDisableRemovesAnOwnedReadOnlyShortcut()
    {
        WithShortcut(path =>
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);

            StartupRegistrationPolicy.RemoveShortcut(path, _ => true);

            Assert.IsFalse(File.Exists(path),
                "Disabling startup must not silently leave an active shortcut.");
        });
    }

    [TestMethod]
    public void ExplicitDisableDoesNotDeleteAForeignShortcut()
    {
        WithShortcut(path =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                StartupRegistrationPolicy.RemoveShortcut(path, _ => false));
            Assert.IsTrue(File.Exists(path));
        });
    }

    [TestMethod]
    [DataRow("DS4Windows managed startup task v1", true, true, false, true)]
    [DataRow("DS4Windows managed startup task v1", false, true, true, false)]
    [DataRow("DS4Windows managed startup task v1", true, false, true, false)]
    [DataRow("", true, true, true, true)]
    [DataRow("", true, true, false, false)]
    [DataRow("", false, true, true, false)]
    [DataRow("Another program", true, true, true, false)]
    public void TaskOwnershipRequiresCurrentUserAndRecognizedRegistration(
        string description, bool currentUser, bool contract, bool legacyProduct, bool expected)
    {
        Assert.AreEqual(expected, StartupRegistrationPolicy.OwnsTask(
            description, currentUser, contract, legacyProduct));
    }

    [TestMethod]
    [DataRow(true, "S-1-5-21-123", "S-1-5-21-123", true)]
    [DataRow(false, "S-1-5-21-123", "S-1-5-21-123", false)]
    [DataRow(true, "S-1-5-21-123", "S-1-5-21-456", false)]
    [DataRow(true, "", "", false)]
    [DataRow(true, null, null, false)]
    public void ElevatedHelperRequiresTheSameNamedAccount(
        bool elevated, string currentSid, string requestedSid, bool expected)
    {
        Assert.AreEqual(expected, StartupRegistrationPolicy.AuthorizesTaskHelper(
            elevated, currentSid, requestedSid));
    }

    [TestMethod]
    public void OpeningSettingsOnlyReadsStartupState()
    {
        var access = new FakeStartupAccess { State = new(false, true) };
        SettingsViewModel view = access.CreateView();

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsTrue(view.RunStartTask);
        Assert.AreEqual(0, access.Changes);
        Assert.AreEqual(0, access.Refreshes);
    }

    [TestMethod]
    [DataRow(true, null, null, "", true, false)]
    [DataRow(false, null, null, "", false, false)]
    [DataRow(false, null, true, "RestartRequired", true, true)]
    [DataRow(false, false, true, "RestartRequired", false, false)]
    [DataRow(false, true, false, "AdministratorRequired", true, true)]
    [DataRow(true, false, true, "", false, false)]
    public void RequestedStartupUsesExplicitChoiceThenSetupThenObservedState(
        bool actualEnabled, bool? userRequested, bool? setupRequested,
        string deferredReason, bool expectedRequested, bool expectedPending)
    {
        StartupRegistrationState state = StartupRegistrationPolicy.ResolveState(
            false, actualEnabled, userRequested, setupRequested, deferredReason);

        Assert.AreEqual(actualEnabled, state.Enabled);
        Assert.AreEqual(expectedRequested, state.RunAtStartupRequested);
        Assert.AreEqual(expectedPending, state.Pending);
    }

    [TestMethod]
    public void DeferredStartupStaysCheckedAndExplainsRestartWithoutChangingTasks()
    {
        var access = new FakeStartupAccess
        {
            State = new(false, false, true, "RestartRequired"),
        };
        SettingsViewModel view = access.CreateView();

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsTrue(view.RunStartTask);
        Assert.IsFalse(view.CanChangeStartupMode);
        StringAssert.Contains(view.StartupStatusText, "restart Windows");
        StringAssert.Contains(view.StartupStatusText, "approve DS4Windows setup if prompted");
        StringAssert.Contains(view.StartupStatusText, "open DS4Windows manually");
        Assert.AreEqual(System.Windows.Visibility.Visible, view.StartupStatusVisibility);
        view.RunStartProg = true;
        Assert.AreEqual(0, access.Changes,
            "A startup shortcut must not bypass setup's pending dependency gate.");
        Assert.AreEqual(0, access.Refreshes);
    }

    [TestMethod]
    public void ExplicitDisableCancelsPendingStartupEvenWhenRemovalFails()
    {
        var access = new FakeStartupAccess
        {
            State = new(false, false, true, "RestartRequired"),
            SavePreferenceBeforeWrite = true,
            WriteFailure = new UnauthorizedAccessException("denied"),
        };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsFalse(view.RunAtStartup);
        Assert.IsFalse(access.CreateView().RunAtStartup);
        Assert.IsFalse(access.State.RunAtStartupRequested);
        Assert.AreEqual(1, access.Errors.Count);
    }

    [TestMethod]
    public void FailedRemovalDistinguishesSavedDisableFromStillActiveRegistration()
    {
        var access = new FakeStartupAccess
        {
            State = new(false, true, true),
            SavePreferenceBeforeWrite = true,
            WriteFailure = new UnauthorizedAccessException("denied"),
        };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsFalse(view.RunAtStartup);
        Assert.IsTrue(access.State.Enabled);
        StringAssert.Contains(view.StartupStatusText, "choice to turn off automatic startup is saved");
        StringAssert.Contains(view.StartupStatusText, "Windows may still open DS4Windows");
        StringAssert.Contains(view.StartupStatusText, "Install / Repair VIIPER");
        Assert.AreEqual(1, access.Errors.Count);
    }

    [TestMethod]
    public void EnablingDeferredStartupSavesIntentWithoutRegisteringAnActiveTask()
    {
        var access = new FakeStartupAccess
        {
            State = new(false, false, false, "RestartRequired"),
            SavePreferenceBeforeWrite = true,
            DeferEnable = true,
        };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = true;

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsTrue(access.CreateView().RunAtStartup);
        Assert.IsFalse(access.State.Enabled);
        Assert.IsTrue(access.State.Pending);
        Assert.AreEqual(0, access.Errors.Count);
    }

    [TestMethod]
    public void UnreadableStartupMetadataIsVisibleAndCannotBeSavedAsAnOptOut()
    {
        var access = new FakeStartupAccess
        {
            State = new(false, false, true, ReadError: "inspection denied"),
        };
        SettingsViewModel view = access.CreateView();

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsFalse(view.CanChangeStartupPreference);
        Assert.IsFalse(view.CanChangeStartupMode);
        StringAssert.Contains(view.StartupStatusText, "could not be checked");
        StringAssert.Contains(view.StartupStatusText, "Restart DS4Windows");
        StringAssert.Contains(view.StartupStatusText, "Log tab");
        StringAssert.Contains(view.StartupStatusText, "open DS4Windows manually");
        Assert.IsFalse(view.StartupStatusText.Contains("inspection denied", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "inspection denied" }, access.Diagnostics);
        view.RunAtStartup = false;
        Assert.AreEqual(0, access.Changes);
        Assert.IsTrue(access.State.RunAtStartupRequested);
    }

    [TestMethod]
    public void LegacySchedulerReadFailureCannotBecomeAnInstallerOptOut()
    {
        IOException error = Assert.ThrowsExactly<IOException>(() =>
            StartupRegistrationPolicy.ResolveRequestedStartup(null,
                () => new(false, false, ReadError: "Scheduler unavailable")));
        StringAssert.Contains(error.InnerException.Message, "Scheduler unavailable");
        Assert.IsFalse(error.Message.Contains("Scheduler unavailable", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RecordedPreferenceDoesNotDependOnTaskSchedulerAvailability(bool requested)
    {
        Assert.AreEqual(requested, StartupRegistrationPolicy.ResolveRequestedStartup(
            requested, () => throw new AssertFailedException(
                "Known startup intent must not be replaced by a Scheduler read.")));
    }

    [TestMethod]
    public void LegacyRebootDeferralKeepsStartupRequestedAndDirectsUserToRepair()
    {
        StartupRegistrationState state = StartupRegistrationPolicy.RecoverLegacySetupDeferral(
            new(false, false), hasSetupRecord: false, exactOwnedDisabledTask: true,
            infrastructureState: "RebootPending");
        var access = new FakeStartupAccess { State = state };
        SettingsViewModel view = access.CreateView();

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsFalse(state.Enabled);
        Assert.IsFalse(state.AllowsTaskRepair);
        StringAssert.Contains(view.StartupStatusText, "Install / Repair VIIPER");
        Assert.IsFalse(view.StartupStatusText.Contains("restart Windows", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(StartupRegistrationPolicy.ResolveRequestedStartup(null, () => state),
            "Built-in repair must not pass SkipStartupTasks for the old setup deferral.");
        Assert.AreEqual(0, access.Changes);
    }

    [TestMethod]
    [DataRow("RepairRequired")]
    [DataRow("UnrecognizedSetupState")]
    [DataRow("")]
    public void PendingStartupNamesTheRepairButtonAndManualFallback(string reason)
    {
        var access = new FakeStartupAccess { State = new(false, false, true, reason) };
        SettingsViewModel view = access.CreateView();

        Assert.IsTrue(view.RunAtStartup, "The checkbox must keep the saved choice.");
        StringAssert.Contains(view.StartupStatusText, "saved but not active yet");
        StringAssert.Contains(view.StartupStatusText, "Install / Repair VIIPER");
        StringAssert.Contains(view.StartupStatusText, "open DS4Windows manually");
        Assert.IsFalse(view.StartupStatusText.Contains("requested", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, access.Changes);
    }

    [TestMethod]
    public void DifferentAdministratorAccountDoesNotSuggestAnElevationRetryLoop()
    {
        var access = new FakeStartupAccess
        {
            State = new(false, false, true, "AdministratorRequired"),
        };
        SettingsViewModel view = access.CreateView();

        Assert.IsTrue(view.RunAtStartup);
        StringAssert.Contains(view.StartupStatusText, "different administrator account");
        StringAssert.Contains(view.StartupStatusText, "Ask your administrator");
        StringAssert.Contains(view.StartupStatusText, "this Windows account");
        StringAssert.Contains(view.StartupStatusText, "open DS4Windows manually");
        Assert.IsFalse(view.StartupStatusText.Contains("repair", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(view.StartupStatusText.Contains("as administrator", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, access.Changes);
    }

    [TestMethod]
    [DataRow(false, false, true, "RebootPending")]
    [DataRow(true, false, true, "RebootPending")]
    [DataRow(null, true, true, "RebootPending")]
    [DataRow(null, false, false, "RebootPending")]
    [DataRow(null, false, true, "Ready")]
    [DataRow(null, false, true, "Failed")]
    [DataRow(null, false, true, "rebootpending")]
    [DataRow(null, false, true, null)]
    public void LegacyMigrationCannotOverridePreferencesOrInferFromUnverifiedState(
        bool? requested, bool hasSetupRecord, bool exactOwnedDisabledTask, string infrastructureState)
    {
        var original = new StartupRegistrationState(false, false, requested);

        StartupRegistrationState actual = StartupRegistrationPolicy.RecoverLegacySetupDeferral(
            original, hasSetupRecord, exactOwnedDisabledTask, infrastructureState);

        Assert.AreEqual(original, actual);
    }

    [TestMethod]
    public void BackendReadFailureRemainsVisibleWithoutBreakingHealthyControllerOutput()
    {
        var status = new DS4Windows.ViiperPrerequisiteStatus
        {
            ViiperInstalled = true,
            ViiperPackageCurrent = true,
            ServerRunning = true,
            UsbipInstalled = true,
            UsbipExecutableSafe = true,
            UsbipDriverFilesSafe = true,
            UsbipRuntimeReady = true,
            ViiperStartupTaskReady = true,
            StartupPreferenceReadError = "inspection denied",
        };

        Assert.IsTrue(status.Ready);
        StringAssert.Contains(status.DisplayText, "VIIPER is ready to use");
        StringAssert.Contains(status.DisplayText, "Automatic startup could not be checked");
        StringAssert.Contains(status.DisplayText, "restart DS4Windows");
        Assert.IsFalse(status.DisplayText.Contains("inspection denied", StringComparison.Ordinal));
        Assert.AreEqual("inspection denied", status.StartupPreferenceReadError,
            "Technical details remain available separately from the main status text.");
    }

    [TestMethod]
    public void SilentRemovalFailureCannotLookDisabled()
    {
        var access = new FakeStartupAccess { State = new(true, false), IgnoreWrites = true };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsTrue(view.RunAtStartup);
        Assert.AreEqual(1, access.Errors.Count);
        Assert.AreEqual(0, access.Refreshes);
        Assert.IsTrue(access.CreateView().RunAtStartup);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeniedOrCanceledTaskRemovalKeepsTheVerifiedSetting(bool canceled)
    {
        var access = new FakeStartupAccess
        {
            State = new(false, true),
            WriteFailure = canceled ? new Win32Exception(1223) : new UnauthorizedAccessException("denied"),
        };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsTrue(view.RunStartTask);
        Assert.AreEqual(1, access.Errors.Count);
        Assert.AreEqual(0, access.Refreshes);
    }

    [TestMethod]
    public void SuccessfulDisableStaysOffWhenSettingsAreReopened()
    {
        var access = new FakeStartupAccess { State = new(true, true) };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsFalse(view.RunAtStartup);
        Assert.IsFalse(access.CreateView().RunAtStartup);
        Assert.AreEqual(1, access.Changes);
        Assert.AreEqual(1, access.Refreshes);
        Assert.AreEqual(0, access.Errors.Count);
    }

    [TestMethod]
    public void RadioUncheckAndChangeNotificationsDoNotWriteStartupEntries()
    {
        var access = new FakeStartupAccess { State = new(true, false) };
        SettingsViewModel view = access.CreateView();
        view.RunStartProg = false; // WPF unchecks the old radio before selecting the new one.
        Assert.AreEqual(0, access.Changes);
        view.RunAtStartupChanged += (_, _) => view.RunAtStartup = false;

        view.RunStartTask = true;

        Assert.IsTrue(view.RunAtStartup);
        Assert.IsTrue(view.RunStartTask);
        Assert.IsFalse(view.RunStartProg);
        Assert.AreEqual(1, access.Changes);
        Assert.AreEqual(1, access.Refreshes);
    }

    [TestMethod]
    public void FailedReinspectionRetainsLastKnownStateAndReportsFailure()
    {
        var access = new FakeStartupAccess { State = new(false, true), FailReadAfterWrite = true };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsTrue(view.RunAtStartup);
        Assert.AreEqual(1, access.Errors.Count);
        Assert.AreEqual(0, access.Refreshes);
    }

    [TestMethod]
    public void FailedBackendRefreshDoesNotUndoVerifiedApplicationDisable()
    {
        var access = new FakeStartupAccess { State = new(false, true), FailRefresh = true };
        SettingsViewModel view = access.CreateView();

        view.RunAtStartup = false;

        Assert.IsFalse(view.RunAtStartup);
        Assert.AreEqual(1, access.Errors.Count);
        Assert.AreEqual(1, access.Refreshes);
    }

    [TestMethod]
    public void RejectedDisableRefreshesTheBoundCheckboxWithoutOpeningAWindow()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var access = new FakeStartupAccess { State = new(true, false), IgnoreWrites = true };
                SettingsViewModel view = access.CreateView();
                var checkbox = new CheckBox();
                BindingOperations.SetBinding(checkbox, ToggleButton.IsCheckedProperty,
                    new Binding(nameof(SettingsViewModel.RunAtStartup))
                    {
                        Source = view,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    });
                Assert.AreEqual(true, checkbox.IsChecked);

                checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                Assert.IsTrue(view.RunAtStartup);
                Assert.AreEqual(true, checkbox.IsChecked,
                    "A rejected removal must not leave the visible checkbox unchecked.");
                Assert.AreEqual(1, access.Changes);
                BindingOperations.ClearAllBindings(checkbox);
            }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Isolated binding check timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void PassiveViiperStartupRefreshCannotRemoveAPendingDisabledTask()
    {
        // Source-bound wiring check only: do not construct TaskService or
        // inspect/change the machine's startup entries in this test process.
        string body = ReadViiperStartupMethod(
            "public static void RefreshSelectedStartupTaskOnLaunch()",
            "public static void RefreshSelectedStartupTaskAfterRunAtStartupChange()");
        Assert.IsFalse(body.Contains("RemoveViiperStartupTask", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("DeleteViiperStartupTask", StringComparison.Ordinal));
        StringAssert.Contains(body, "ViiperStartupTaskPolicy.RefreshOnLaunch(");
        StringAssert.Contains(body, "CanRepairViiperStartupTask,");
        DS4Windows.ViiperStartupTaskPolicy.RefreshOnLaunch(false,
            @"C:\Program Files\DS4Windows\VIIPER\viiper.exe",
            () => @"C:\Program Files\DS4Windows\VIIPER\viiper.exe",
            _ => true, () => false, _ => { },
            _ => throw new AssertFailedException(
                "Passive repair must stop before changing a task when startup is not enabled."));
    }

    [TestMethod]
    public void ExplicitViiperStartupDisableChecksRemovalAndSurfacesFailure()
    {
        string body = ReadViiperStartupMethod(
            "public static void RefreshSelectedStartupTaskAfterRunAtStartupChange()",
            "public static bool LaunchInstaller(");
        StringAssert.Matches(body, new Regex(
            @"if\s*\(!DS4WinWPF\.StartupMethods\.IsRunAtStartupRequested\(\)\)\s*\{\s*" +
            @"if\s*\(!RemoveViiperStartupTask\(requestElevation:\s*true\)\)\s*" +
            @"throw new IOException\("));
        StringAssert.Contains(body, "RefreshSelectedStartupTaskOnLaunch();");
    }

    private static string ReadViiperStartupMethod(string signature, string nextSignature)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "DS4Windows", "DS4Control",
                "Viiper", "ViiperSetupManager.cs");
            if (!File.Exists(path)) continue;
            string source = File.ReadAllText(path);
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "The startup entry point was not found.");
            int end = source.IndexOf(nextSignature, start + signature.Length, StringComparison.Ordinal);
            Assert.IsTrue(end > start, "The next method boundary was not found.");
            // Ignore explanatory comments without flattening statements.
            return Regex.Replace(source[start..end], @"//[^\r\n]*", string.Empty);
        }
        throw new AssertFailedException("The VIIPER startup source was not found.");
    }

    private sealed class FakeStartupAccess
    {
        internal StartupRegistrationState State;
        internal bool IgnoreWrites;
        internal bool FailReadAfterWrite;
        internal bool FailRefresh;
        internal bool SavePreferenceBeforeWrite;
        internal bool DeferEnable;
        internal Exception WriteFailure;
        internal int Changes;
        internal int Refreshes;
        internal List<string> Errors = new();
        internal List<string> Diagnostics = new();

        internal SettingsViewModel CreateView() => new(Read, Change, Refresh, Errors.Add, Diagnostics.Add);

        private StartupRegistrationState Read()
        {
            if (FailReadAfterWrite && Changes > 0) throw new IOException("inspection failed");
            return State;
        }

        private void Change(StartupRegistrationMode mode)
        {
            Changes++;
            if (SavePreferenceBeforeWrite)
                State = State with { Requested = mode != StartupRegistrationMode.Disabled };
            if (WriteFailure != null) throw WriteFailure;
            if (DeferEnable && mode != StartupRegistrationMode.Disabled) return;
            if (!IgnoreWrites)
                State = new(mode == StartupRegistrationMode.Program, mode == StartupRegistrationMode.Task);
        }

        private void Refresh()
        {
            Refreshes++;
            if (FailRefresh) throw new IOException("backend refresh failed");
        }
    }

    private static void WithShortcut(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ds4w-startup-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "isolated-test.lnk");
        File.WriteAllText(path, "No actual Windows shortcut or startup entry.");
        try { action(path); }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            Directory.Delete(directory);
        }
    }
}
