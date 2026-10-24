using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace DS4Windows;

internal readonly record struct ViiperBatteryStatus(
    [property: JsonPropertyName("percent")] byte Percent,
    [property: JsonPropertyName("charging")] bool Charging,
    [property: JsonPropertyName("externalPower")] bool ExternalPower,
    [property: JsonPropertyName("complete")] bool Complete)
{
    [JsonPropertyName("version")] public int Version => 1;

    internal static bool TryRead(DS4Device source, out ViiperBatteryStatus status)
    {
        status = default;
        if (source == null || source.IsRemoved || !source.BatteryReadingAvailable ||
            (source.FeatureSet & VidPidFeatureSet.NoBatteryReading) != 0) return false;
        int battery = source.getBattery();
        if (battery < 0 || battery > 100) return false;
        bool powered = source.isCharging() || source.ConnectionType == ConnectionType.USB;
        bool complete = powered && battery == 100 && source.BatteryChargeComplete;
        status = new((byte)battery, source.isCharging() && !complete, powered, complete);
        return true;
    }
}

/// <summary>Rare power changes run off the report thread. One owner per virtual
/// stream is joined before that stream can be removed or replaced.</summary>
internal sealed class ViiperBatteryStatusBridge : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private int disposed;

    internal ViiperBatteryStatusBridge(Func<DS4Device> source,
        Action<ViiperBatteryStatus> publish, Action<Exception> onError, TimeSpan? pollInterval = null)
    {
        worker = Task.Run(async () =>
        {
            DS4Device previousSource = null;
            ViiperBatteryStatus? previous = null;
            bool failureLogged = false;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    DS4Device current = source();
                    if (ViiperBatteryStatus.TryRead(current, out var status) &&
                        (!ReferenceEquals(current, previousSource) || previous != status) &&
                        ReferenceEquals(current, source()) && !stop.IsCancellationRequested)
                    {
                        try
                        {
                            publish(status);
                            previous = status;
                            previousSource = current;
                            failureLogged = false;
                        }
                        catch (Exception ex) when (ex is IOException || ex is System.Net.Sockets.SocketException || ex is ObjectDisposedException || ex is System.Text.Json.JsonException)
                        {
                            if (!failureLogged) onError(ex);
                            failureLogged = true;
                        }
                    }
                    await Task.Delay(pollInterval ?? TimeSpan.FromSeconds(1), stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        worker.GetAwaiter().GetResult();
        stop.Dispose();
    }
}
