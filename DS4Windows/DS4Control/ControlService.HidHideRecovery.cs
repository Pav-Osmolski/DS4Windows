using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DS4WinWPF.DS4Control;

namespace DS4Windows;

internal sealed record HidHideRecoveryAudit(HidHideConfigurationSnapshot Snapshot,
    IReadOnlyList<string> SelectableIds);

public partial class ControlService
{
    private HidHideOwnershipStore hidHideOwnership;
    private HidHideOwnershipStore HidHideOwnership => LazyInitializer.EnsureInitialized(
        ref hidHideOwnership, HidHideOwnershipStore.Create);

    private static bool TrySetHidHideBlacklist(HidHideAPIDevice device, List<string> expected) =>
        device.SetBlacklist(expected) && device.TryGetBlacklist(out var actual) &&
        expected.SequenceEqual(actual, StringComparer.Ordinal);

    private void ReleasePersistentHidHideRules(HidHideAPIDevice device,
        HidHideOwnershipStore.Transaction ownership, IEnumerable<string> identities, string reason)
    {
        var candidates = identities.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length == 0) return;
        if (!device.IsOpen() || !device.TryGetBlacklist(out var blacklist))
            throw new IOException("The HidHide device list could not be read.");
        var removable = new HashSet<string>(ownership.Release(candidates), StringComparer.OrdinalIgnoreCase);
        int removed = blacklist.RemoveAll(removable.Contains);
        if (removed > 0 && !TrySetHidHideBlacklist(device, blacklist))
            throw new IOException("The HidHide device list could not be verified after cleanup.");
        var reassert = hidHideManagedDevices.CompletePersistentRelease(candidates);
        if (reassert.Count > 0)
        {
            ownership.Claim(reassert, blacklist);
            foreach (string id in reassert)
                if (!blacklist.Contains(id, StringComparer.OrdinalIgnoreCase)) blacklist.Add(id);
            bool succeeded = TrySetHidHideBlacklist(device, blacklist);
            hidHideManagedDevices.CompletePersistentReassert(reassert, succeeded);
            if (!succeeded) throw new IOException("HidHide reconnect protection could not be verified.");
            ownership.ConfirmAdded(reassert);
        }
        ownership.ConfirmAbsent(blacklist);
        RefreshHidHideAttributes(device);
        if (removed > 0)
            LogDebug($"Released {removed} DS4Windows-managed HidHide blacklist entries ({reason}).");
    }

    private void RecoverHidHideOwnedRules()
    {
        if (PortableLabContext.IsActive || !Global.hidHideInstalled) return;
        try
        {
            lock (hidHideDriverMutationLock)
            {
                using var ownership = HidHideOwnership.Open();
                string[] stale = ownership.StaleIds;
                if (stale.Length == 0) return;
                using var device = new HidHideAPIDevice(exclusive: true);
                if (!HidHideConfigurationGuard.TryRead(device, out var snapshot))
                    throw new IOException("HidHide settings could not be read safely.");
                string backup = HidHideConfigurationGuard.WriteBackup(HidHideOwnership.RecoveryDirectory, snapshot, "Interrupted-session hiding recovery");
                ReleasePersistentHidHideRules(device, ownership, stale, "interrupted-session recovery");
                LogDebug($"Recovered HidHide ownership from an interrupted DS4Windows session. Configuration backup: {backup}");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"HidHide automatic recovery could not finish: {ex.Message}. Use Settings > Components > Recover hidden controllers.", true);
        }
    }

    internal HidHideRecoveryAudit InspectHiddenControllerRecovery()
    {
        lock (serviceLifecycleLock)
        lock (hidHideDriverMutationLock)
        {
            if (running || inServiceTask || PortableLabContext.IsActive)
                throw new InvalidOperationException("Stop DS4Windows before recovering hidden controllers.");
            using var ownership = HidHideOwnership.Open();
            using var device = new HidHideAPIDevice(writeAccess: false);
            if (!HidHideConfigurationGuard.TryRead(device, out var snapshot))
                throw new IOException("HidHide settings could not be read safely.");
            return new(snapshot, snapshot.Devices.Where(id => !ownership.IsProtected(id)).ToArray());
        }
    }

    internal string RecoverSelectedHiddenControllers(HidHideRecoveryAudit audit, IEnumerable<string> selection)
    {
        lock (serviceLifecycleLock)
        lock (hidHideDriverMutationLock)
        {
            if (running || inServiceTask || PortableLabContext.IsActive)
                throw new InvalidOperationException("Stop DS4Windows before recovering hidden controllers.");
            var selected = new HashSet<string>(selection, StringComparer.OrdinalIgnoreCase);
            if (audit == null || selected.Count == 0 || selected.Any(id =>
                    !audit.SelectableIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Select a hidden controller from the current list.");
            using var ownership = HidHideOwnership.Open();
            using var device = new HidHideAPIDevice(exclusive: true);
            if (selected.Any(ownership.IsProtected) ||
                !HidHideConfigurationGuard.TryRead(device, out var current) || !audit.Snapshot.Matches(current))
                throw new InvalidOperationException("HidHide settings changed. Reopen recovery and check the device list again.");
            string backup = HidHideConfigurationGuard.WriteBackup(HidHideOwnership.RecoveryDirectory, current, "User-selected hidden-controller recovery");
            var remaining = current.Devices.Where(id => !selected.Contains(id)).ToList();
            if (!TrySetHidHideBlacklist(device, remaining))
                throw new IOException("HidHide recovery could not be verified. Configuration backup: " + backup);
            ownership.ConfirmAbsent(remaining);
            LogDebug($"Restored {selected.Count} explicitly selected HidHide entries. Configuration backup: {backup}");
            return backup;
        }
    }
}
