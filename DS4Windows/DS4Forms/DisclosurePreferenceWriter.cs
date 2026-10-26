using System;
using System.Threading.Tasks;

namespace DS4WinWPF.DS4Forms;

// One writer, with only the latest requested state pending behind it. Disk
// latency must never enter a disclosure's input or layout event handlers.
internal sealed class DisclosurePreferenceWriter
{
    private readonly object gate = new();
    private readonly Action<bool> save;
    private readonly Action<Exception> failed;
    private bool? pending, lastSaved;
    private long revision;
    private bool running;
    private Task worker = Task.CompletedTask;

    internal DisclosurePreferenceWriter(Action<bool> save, Action<Exception> failed)
    {
        this.save = save ?? throw new ArgumentNullException(nameof(save));
        this.failed = failed ?? throw new ArgumentNullException(nameof(failed));
    }

    internal void Queue(bool expanded)
    {
        lock (gate)
        {
            pending = expanded;
            revision++;
            if (running) return;
            running = true;
            worker = Task.Run(Drain);
        }
    }

    internal void Initialize(bool saved)
    {
        lock (gate) { if (!running) lastSaved = saved; }
    }

    internal Task Saved { get { lock (gate) return worker; } }

    private async Task Drain()
    {
        while (true)
        {
            bool value;
            long observed;
            lock (gate) observed = revision;
            // Wait for a quiet interval. Returning to the saved state during
            // rapid open/close clicks must not touch disk at all.
            await Task.Delay(250).ConfigureAwait(false);
            lock (gate)
            {
                if (observed != revision) continue;
                if (!pending.HasValue) { running = false; return; }
                value = pending.Value;
                pending = null;
                if (lastSaved == value) { running = false; return; }
            }
            try
            {
                save(value);
                lock (gate) lastSaved = value;
            }
            catch (Exception error)
            {
                // Failure to remember presentation state must not undo the
                // visible change or show a modal dialog during navigation.
                try { failed(error); } catch { }
            }
            lock (gate) { if (!pending.HasValue) { running = false; return; } }
        }
    }
}
