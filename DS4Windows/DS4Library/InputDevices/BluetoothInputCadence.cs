namespace DS4Windows.InputDevices;

// Measure delivered input across a time window. A burst of already buffered
// reports cannot replace the entire window in a few microseconds.
internal struct BluetoothInputCadence
{
    private long windowStart;
    private int intervals;
    private bool hasStart;

    internal void Reset() => this = default;

    internal bool Observe(long timestamp, long frequency, out double milliseconds)
    {
        milliseconds = 0;
        if (!hasStart || timestamp < windowStart)
        {
            windowStart = timestamp;
            intervals = 0;
            hasStart = true;
            return false;
        }

        intervals++;
        long elapsed = timestamp - windowStart;
        if (elapsed < frequency / 4) return false;

        milliseconds = elapsed * 1000.0 / frequency / intervals;
        windowStart = timestamp;
        intervals = 0;
        return true;
    }
}
