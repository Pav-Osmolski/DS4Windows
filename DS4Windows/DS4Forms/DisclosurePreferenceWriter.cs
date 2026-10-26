using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DS4WinWPF.DS4Forms;

// One writer, with only the latest requested state pending behind it. Disk
// latency must never enter a disclosure's input or layout event handlers.
internal class LatestPreferenceWriter<T>
{
    private readonly object gate = new();
    private readonly Action<T> save;
    private readonly Action<Exception> failed;
    private T pending, lastSaved;
    private bool hasPending, hasSaved;
    private long revision;
    private bool running;
    private Task worker = Task.CompletedTask;

    internal LatestPreferenceWriter(Action<T> save, Action<Exception> failed)
    {
        this.save = save ?? throw new ArgumentNullException(nameof(save));
        this.failed = failed ?? throw new ArgumentNullException(nameof(failed));
    }

    internal void Queue(T expanded)
    {
        lock (gate)
        {
            pending = expanded;
            hasPending = true;
            revision++;
            if (running) return;
            running = true;
            worker = Task.Run(Drain);
        }
    }

    internal void Initialize(T saved)
    {
        lock (gate) { if (!running) { lastSaved = saved; hasSaved = true; } }
    }

    internal Task Saved { get { lock (gate) return worker; } }

    private async Task Drain()
    {
        while (true)
        {
            T value;
            long observed;
            lock (gate) observed = revision;
            // Wait for a quiet interval. Returning to the saved state during
            // rapid open/close clicks must not touch disk at all.
            await Task.Delay(250).ConfigureAwait(false);
            lock (gate)
            {
                if (observed != revision) continue;
                if (!hasPending) { running = false; return; }
                value = pending;
                hasPending = false;
                if (hasSaved && EqualityComparer<T>.Default.Equals(lastSaved, value)) { running = false; return; }
            }
            try
            {
                save(value);
                lock (gate) { lastSaved = value; hasSaved = true; }
            }
            catch (Exception error)
            {
                // Failure to remember presentation state must not undo the
                // visible change or show a modal dialog during navigation.
                try { failed(error); } catch { }
            }
            lock (gate) { if (!hasPending) { running = false; return; } }
        }
    }
}

internal sealed class DisclosurePreferenceWriter : LatestPreferenceWriter<bool>
{
    internal DisclosurePreferenceWriter(Action<bool> save, Action<Exception> failed) : base(save, failed) { }
}
