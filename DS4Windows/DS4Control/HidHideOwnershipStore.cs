using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace DS4Windows;

// Cold driver-configuration work only. A lease is held for the process lifetime,
// not a PID (which Windows can reuse). All copies belonging to this Windows user
// serialize their write-ahead journal through the same file lock.
internal sealed class HidHideOwnershipStore : IDisposable
{
    internal sealed class State
    {
        public int Version { get; set; } = 1;
        public List<Rule> Rules { get; set; } = new();
    }

    internal sealed class Rule
    {
        public string InstanceId { get; set; }
        public bool Confirmed { get; set; }
        public List<string> Owners { get; set; } = new();
    }

    private readonly string directory;
    private readonly string owner = Guid.NewGuid().ToString("N");
    private readonly FileStream lease;
    private readonly Func<string, bool> live;

    internal static HidHideOwnershipStore Create() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DS4Windows", "HidHideOwnership"));

    internal HidHideOwnershipStore(string directory, Func<string, bool> isLive = null)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new IOException("A writable local application-data folder is required for HidHide ownership.");
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        lease = new FileStream(LeasePath(owner), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.Read);
        live = isLive ?? IsLive;
    }

    internal string Owner => owner;
    internal string RecoveryDirectory => directory;
    private string LeasePath(string id) => Path.Combine(directory, id + ".lease");

    private bool IsLive(string id)
    {
        if (id == owner) return true;
        try
        {
            using var probe = new FileStream(LeasePath(id), FileMode.Open,
                FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; } // Locked or uncertain: never reclaim.
        catch (UnauthorizedAccessException) { return true; }
    }

    internal Transaction Open()
    {
        FileStream gate = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                gate = new FileStream(Path.Combine(directory, "ownership.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new Transaction(this, gate);
            }
            catch (IOException) when (gate == null && attempt < 20)
            {
                Thread.Sleep(25);
            }
            catch { gate?.Dispose(); throw; }
        }
    }

    public void Dispose() => lease.Dispose();

    internal sealed class Transaction : IDisposable
    {
        private readonly HidHideOwnershipStore store;
        private readonly FileStream gate;
        private readonly string path;
        private readonly State state;

        internal Transaction(HidHideOwnershipStore store, FileStream gate)
        {
            this.store = store;
            this.gate = gate;
            path = Path.Combine(store.directory, "ownership.json");
            if (!File.Exists(path)) state = new();
            else
            {
                if (new FileInfo(path).Length > 1024 * 1024)
                    throw new InvalidDataException("HidHide ownership journal is too large.");
                state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            }
            if (state == null || state.Version != 1 || state.Rules == null ||
                state.Rules.Count > 2048 || state.Rules.Any(r => r == null ||
                    string.IsNullOrWhiteSpace(r.InstanceId) || r.InstanceId.Length > 1024 ||
                    r.Owners == null || r.Owners.Count > 128 ||
                    r.Owners.Any(o => !Guid.TryParseExact(o, "N", out _))) ||
                state.Rules.Select(r => r.InstanceId).Distinct(
                    StringComparer.OrdinalIgnoreCase).Count() != state.Rules.Count)
                throw new InvalidDataException("HidHide ownership journal is invalid; no rules were changed.");
        }

        internal string[] CurrentOwnedIds => state.Rules.Where(r =>
            r.Owners.Contains(store.owner)).Select(r => r.InstanceId).ToArray();

        internal bool HasOtherLiveOwners => state.Rules.Any(r =>
            r.Owners.Any(o => o != store.owner && store.live(o)));

        // Returns only proven ownership. An already-present untracked rule is
        // deliberately never adopted. New rules are persisted BEFORE the IOCTL.
        internal string[] Claim(IEnumerable<string> identities, IEnumerable<string> blacklist)
        {
            var existing = new HashSet<string>(blacklist, StringComparer.OrdinalIgnoreCase);
            var claimed = new List<string>();
            foreach (string id in identities.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Rule rule = state.Rules.Find(r => string.Equals(r.InstanceId, id,
                    StringComparison.OrdinalIgnoreCase));
                if (rule == null)
                {
                    if (existing.Contains(id)) continue;
                    rule = new() { InstanceId = id };
                    state.Rules.Add(rule);
                }
                // An interrupted, unconfirmed write is not proof that a rule
                // now present in the driver was created by us. Offer explicit
                // recovery rather than silently claiming/deleting that rule.
                else if (!rule.Confirmed && existing.Contains(id)) continue;
                if (!rule.Owners.Contains(store.owner)) rule.Owners.Add(store.owner);
                claimed.Add(id);
            }
            Save();
            return claimed.ToArray();
        }

        internal string[] Release(IEnumerable<string> identities)
        {
            var ids = new HashSet<string>(identities, StringComparer.OrdinalIgnoreCase);
            foreach (Rule rule in state.Rules.Where(r => ids.Contains(r.InstanceId)))
                rule.Owners.RemoveAll(o => o == store.owner || !store.live(o));
            // Empty records remain write-ahead tombstones until driver readback
            // proves removal. A crash or failed IOCTL must not erase our proof.
            Save();
            return state.Rules.Where(r => ids.Contains(r.InstanceId) &&
                r.Confirmed && r.Owners.Count == 0).Select(r => r.InstanceId).ToArray();
        }

        internal string[] StaleIds => state.Rules.Where(r =>
            r.Confirmed && !r.Owners.Any(store.live)).Select(r => r.InstanceId).ToArray();

        internal void ConfirmAdded(IEnumerable<string> identities)
        {
            var ids = new HashSet<string>(identities, StringComparer.OrdinalIgnoreCase);
            foreach (Rule rule in state.Rules.Where(r => ids.Contains(r.InstanceId) &&
                         r.Owners.Contains(store.owner))) rule.Confirmed = true;
            Save();
        }

        internal bool IsProtected(string id) => state.Rules.Any(r =>
            string.Equals(r.InstanceId, id, StringComparison.OrdinalIgnoreCase) &&
            r.Owners.Any(store.live));

        internal void ConfirmAbsent(IEnumerable<string> blacklist)
        {
            var present = new HashSet<string>(blacklist, StringComparer.OrdinalIgnoreCase);
            state.Rules.RemoveAll(r => !present.Contains(r.InstanceId) &&
                !r.Owners.Any(store.live));
            Save();
        }

        private void Save()
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(output, state);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Dispose() => gate.Dispose();
    }
}
