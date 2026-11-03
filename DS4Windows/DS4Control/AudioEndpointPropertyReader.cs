using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace DS4Windows;

// NAudio's key indexer (and Contains) walks every key and calls the native Count
// getter on each iteration. Repeating it for several identity fields makes an
// otherwise read-only status poll surprisingly expensive on large property stores.
// Take one fresh, bounded pass. No endpoint identity/state is cached across polls.
internal static class AudioEndpointPropertyReader
{
    internal static Dictionary<PropertyKey, string> Read(MMDevice endpoint,
        params PropertyKey[] requested)
    {
        var store = endpoint.Properties;
        return Read(store.Count, store.Get,
            index => store[index].Value?.ToString() ?? string.Empty, requested);
    }

    internal static Dictionary<PropertyKey, string> Read(int count,
        Func<int, PropertyKey> keyAt, Func<int, string> valueAt,
        params PropertyKey[] requested)
    {
        var remaining = new HashSet<PropertyKey>(requested);
        var values = new Dictionary<PropertyKey, string>();
        for (int index = 0; index < count && remaining.Count != 0; index++)
        {
            try
            {
                var key = keyAt(index);
                if (!remaining.Contains(key)) continue;
                values[key] = valueAt(index) ?? string.Empty;
                remaining.Remove(key);
            }
            catch
            {
                // A stale/unreadable property must not hide other valid fields.
                // A future status poll reads a new snapshot and retries it.
            }
        }
        return values;
    }
}
