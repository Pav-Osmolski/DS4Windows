using System.Collections.Generic;
using WixToolset.BootstrapperApplicationApi;

namespace DS4Windows.Bootstrapper;

internal enum OptionalDriverPresence { Unknown, Missing, Installed }

// Burn's detected package state owns presence; never confuse a cached download
// with an installed driver, or preselect installation after an unknown result.
internal sealed class OptionalDriverSelection
{
    internal OptionalDriverPresence HidHide { get; private set; }
    internal OptionalDriverPresence FakerInput { get; private set; }
    internal bool InstallHidHide { get; set; }
    internal bool InstallFakerInput { get; set; }
    private bool initialized;

    internal void Detect(IReadOnlyDictionary<string, PackageState> packages, bool relatedFakerInput)
    {
        HidHide = Presence(packages, "HidHide", false);
        FakerInput = Presence(packages, "FakerInput", relatedFakerInput);
        if (!initialized)
        {
            InstallHidHide = HidHide == OptionalDriverPresence.Missing;
            InstallFakerInput = false;
            initialized = true;
        }
        // Re-detection must preserve explicit choices, except that installation
        // of an already-present shared driver is never offered as an update.
        if (HidHide == OptionalDriverPresence.Installed) InstallHidHide = false;
        if (FakerInput == OptionalDriverPresence.Installed) InstallFakerInput = false;
    }

    internal static OptionalDriverPresence Presence(IReadOnlyDictionary<string, PackageState> packages, string id, bool related)
    {
        if (related) return OptionalDriverPresence.Installed;
        if (!packages.TryGetValue(id, out var state)) return OptionalDriverPresence.Unknown;
        return state switch
        {
            PackageState.Present or PackageState.Superseded => OptionalDriverPresence.Installed,
            PackageState.Absent => OptionalDriverPresence.Missing,
            _ => OptionalDriverPresence.Unknown
        };
    }

    internal static string Status(OptionalDriverPresence presence, bool selected, bool uninstall = false) => presence switch
    {
        OptionalDriverPresence.Installed => "Installed — kept unchanged",
        OptionalDriverPresence.Missing when !uninstall && selected => "Not installed — will install",
        OptionalDriverPresence.Missing => "Not installed",
        _ => "Could not determine installation status"
    };

    internal static bool PreserveExisting(LaunchAction action, string id, IReadOnlyDictionary<string, PackageState> packages, bool relatedFakerInput) =>
        (action == LaunchAction.Install || action == LaunchAction.Repair) &&
        (id == "HidHide" || id == "FakerInput") &&
        Presence(packages, id, id == "FakerInput" && relatedFakerInput) == OptionalDriverPresence.Installed;
}
