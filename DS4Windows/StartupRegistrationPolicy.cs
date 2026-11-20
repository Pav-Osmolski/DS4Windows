using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace DS4WinWPF;

internal static class StartupRegistrationPolicy
{
    internal const string ManagedTaskDescription = "DS4Windows managed startup task v1";

    internal static string ReadTaskPrincipalUserId(string taskXml)
    {
        if (string.IsNullOrWhiteSpace(taskXml)) return null;
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        try
        {
            using var input = new StringReader(taskXml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 65536,
            });
            var task = XDocument.Load(reader).Root;
            var principals = task?.Element(ns + "Principals");
            if (principals == null) return null;
            using var entries = principals.Elements().GetEnumerator();
            if (!entries.MoveNext()) return null;
            XElement principal = entries.Current;
            return principal.Name == ns + "Principal" && !entries.MoveNext()
                ? principal.Element(ns + "UserId")?.Value
                : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    internal static bool ShouldRepairTask(bool exists, bool enabled,
        bool owned, bool matchesCurrentConfiguration, bool requested = true,
        bool setupDeferred = false) =>
        requested && !setupDeferred && exists && enabled && owned && !matchesCurrentConfiguration;

    internal static bool OwnsTask(string description, bool currentUser,
        bool expectedContract, bool recognizedLegacyExecutable) =>
        currentUser && expectedContract &&
        (string.Equals(description, ManagedTaskDescription, StringComparison.Ordinal) ||
         (string.IsNullOrWhiteSpace(description) && recognizedLegacyExecutable));

    internal static bool AuthorizesTaskHelper(bool elevated, string currentSid, string requestedSid) =>
        elevated && !string.IsNullOrWhiteSpace(currentSid) &&
        string.Equals(currentSid, requestedSid, StringComparison.OrdinalIgnoreCase);

    internal static StartupRegistrationState ResolveState(bool program, bool task,
        bool? userRequested, bool? setupRequested, string deferredReason) =>
        new(program, task, userRequested ?? setupRequested, deferredReason);

    internal static string ResolveDeferredReason(string reason, string infrastructureState) =>
        infrastructureState == "Ready" && reason is ("RepairRequired" or "AdministratorRequired")
            ? null : reason;

    internal static bool ResolveRequestedStartup(bool? recordedRequest,
        Func<StartupRegistrationState> readState)
    {
        if (recordedRequest.HasValue) return recordedRequest.Value;
        StartupRegistrationState state = readState();
        if (state.ReadError != null)
            throw new IOException(state.StatusText, new IOException(state.ReadError));
        return state.RunAtStartupRequested;
    }

    internal static StartupRegistrationState RecoverLegacySetupDeferral(
        StartupRegistrationState state, bool hasSetupRecord,
        bool exactOwnedDisabledTask, string infrastructureState)
    {
        if (state.Requested.HasValue || state.Enabled || state.ReadError != null ||
            hasSetupRecord || !exactOwnedDisabledTask ||
            !string.Equals(infrastructureState, "RebootPending", StringComparison.Ordinal))
            return state;

        // Older setup disabled its verified task without recording a separate
        // preference or scheduling a continuation. Preserve that intent, but
        // require explicit repair because no trusted resume snapshot exists.
        return state with { Requested = true, DeferredReason = "RepairRequired" };
    }

    internal static void RemoveShortcut(string path, Func<string, bool> isOwned)
    {
        if (!File.Exists(path)) return;
        if (!isOwned(path))
            throw new InvalidOperationException(
                "The startup shortcut belongs to another application and was not changed.");
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The startup shortcut is a filesystem link and was not changed.");
        try
        {
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
            if (File.Exists(path))
                throw new IOException("The startup shortcut remained after removal.");
        }
        catch
        {
            if (File.Exists(path))
            {
                try { File.SetAttributes(path, attributes); } catch { }
            }
            throw;
        }
    }
}

internal enum StartupRegistrationMode { Disabled, Program, Task }

internal readonly record struct StartupRegistrationState(bool Program, bool Task,
    bool? Requested = null, string DeferredReason = null, string ReadError = null,
    bool TaskConflict = false)
{
    internal bool Enabled => Program || Task;
    internal bool RunAtStartupRequested => Requested ?? Enabled;
    internal bool SetupDeferred => !string.IsNullOrWhiteSpace(DeferredReason);
    internal bool Pending => RunAtStartupRequested && !Enabled && SetupDeferred;
    internal bool AllowsTaskRepair => ReadError == null && RunAtStartupRequested && Enabled && !SetupDeferred;
    internal bool Matches(StartupRegistrationMode mode) => ReadError == null && (mode switch
    {
        StartupRegistrationMode.Disabled => !Enabled && !RunAtStartupRequested,
        StartupRegistrationMode.Program => RunAtStartupRequested &&
            (Program && !Task || Pending),
        StartupRegistrationMode.Task => RunAtStartupRequested &&
            (Task && !Program || Pending),
        _ => false,
    });

    internal string StatusText => ReadError != null
        ? "Automatic startup could not be checked. Restart DS4Windows to try again; details are in the Log tab. You can still open DS4Windows manually."
        : !RunAtStartupRequested && Enabled
        ? "Your choice to turn off automatic startup is saved, but Windows may still open DS4Windows when you sign in. Check the Log tab for details."
        : TaskConflict
        ? "Another or unrecognized RunDS4Windows task exists. DS4Windows leaves it alone. Review it in Windows Task Scheduler if you want to change whether it runs."
        : !RunAtStartupRequested || Enabled ? string.Empty
        : DeferredReason switch
        {
            "RestartRequired" => "Automatic startup is waiting for setup to finish. Save your work, restart Windows, and approve DS4Windows setup if prompted. You can still open DS4Windows manually.",
            "AdministratorRequired" => "Automatic startup could not be enabled because setup used a different administrator account. Ask your administrator to review startup access for this Windows account, or open DS4Windows manually.",
            _ => SetupDeferred
                ? "Automatic startup is saved but not active yet. Go to Settings > Components and select Install / Repair VIIPER to finish setup. You can still open DS4Windows manually."
                : "Automatic startup is saved but not active yet. Turn Run at Startup off and back on to register it. You can still open DS4Windows manually.",
        };
}

internal readonly record struct StartupRegistrationChangeResult(
    bool Success, StartupRegistrationState State, string Error);

internal static class StartupRegistrationChange
{
    internal static StartupRegistrationChangeResult Apply(
        StartupRegistrationMode requested, StartupRegistrationState previous,
        Func<StartupRegistrationState> read, Action<StartupRegistrationMode> change)
    {
        try
        {
            change(requested);
            StartupRegistrationState actual = read();
            return actual.Matches(requested)
                ? new(true, actual, null)
                : new(false, actual, "Windows did not confirm the requested startup setting.");
        }
        catch (Exception error)
        {
            // Never show a failed write as a saved preference. If inspection
            // also fails, retain the last verified state rather than guess.
            StartupRegistrationState actual = previous;
            try { actual = read(); }
            catch (Exception inspectionError)
            {
                actual = previous with { ReadError = inspectionError.Message };
            }
            return new(false, actual, error.Message);
        }
    }
}
