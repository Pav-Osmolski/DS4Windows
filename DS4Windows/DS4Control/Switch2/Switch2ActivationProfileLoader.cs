using System;
using System.IO;
using System.Linq;
using DS4Windows.DS4Control;

namespace DS4Windows.Switch2;

/// <summary>
/// Cold activation-only load: validate before resetting live mappings. A blank
/// unlinked slot may use the existing Default profile; explicit selections
/// never fall back. The caller still owns the exact slot staging transaction.
/// </summary>
internal static class Switch2ActivationProfileLoader
{
    internal static bool TryLoad(int slot, ControlService service, out string diagnostic)
    {
        long revision = Global.BeginProfileSwitchRevision(slot);
        using var mutation = ProfileMutationGate.Enter(slot);
        bool linked = Global.linkedProfileCheck[slot];
        string selected = Global.ProfilePath[slot];
        bool useDefault = string.IsNullOrEmpty(selected) && !linked;
        string name = useDefault ? "Default" : selected;
        string source = linked ? "linked" : useDefault ? "unassigned-default" : "saved";
        string context = $"slot={slot + 1};source={source};profile=\"{LogName(name)}\"";
        diagnostic = context;
        if (!Global.IsCurrentProfileSwitchRevision(slot, revision))
        {
            diagnostic += ";failure=Superseded";
            return false;
        }
        if (string.IsNullOrEmpty(name))
        {
            diagnostic += ";failure=UnassignedLinkedProfile";
            return false;
        }

        string path = Path.Combine(Global.appdatapath, "Profiles", name + ".xml");
        if (!PreparedProfileLoad.TryPrepare(path, slot, out var prepared,
                out var failure, out _))
        {
            diagnostic += $";failure={failure}";
            return false;
        }
        if (!Global.IsCurrentProfileSwitchRevision(slot, revision))
        {
            diagnostic += ";failure=Superseded";
            return false;
        }
        bool loaded = Global.store.ApplyPreparedProfileNew(prepared, false, service,
            out bool changed, xinputChange: false, postLoad: false,
            transitionRevision: revision);
        if (changed && Global.IsCurrentProfileSwitchRevision(slot, revision))
        {
            Global.tempprofilename[slot] = string.Empty;
            Global.useTempProfile[slot] = false;
            Global.tempprofileDistance[slot] = false;
        }
        if (!loaded || !Global.IsCurrentProfileSwitchRevision(slot, revision))
        {
            diagnostic += ";failure=Superseded";
            return false;
        }
        if (useDefault)
        {
            Global.ProfilePath[slot] = Global.OlderProfilePath[slot] = name;
            service.LogDebug($"Switch 2 controller slot #{slot + 1} had no saved profile; loaded the existing Default profile.");
        }
        diagnostic += ";loaded";
        return true;
    }

    // Profile names are user-controlled; keep diagnostics on one log line.
    private static string LogName(string name) => new((name ?? string.Empty)
        .Select(c => char.IsControl(c) || c == '"' ? '?' : c).ToArray());
}
