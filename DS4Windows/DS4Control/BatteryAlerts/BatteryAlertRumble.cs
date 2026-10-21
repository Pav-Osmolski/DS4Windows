using System;
using System.Threading;

namespace DS4Windows;

internal sealed class BatteryAlertRumbleLease : IDisposable
{
    private Action release;
    internal BatteryAlertRumbleLease(Action release) => this.release = release;
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}

public partial class DS4Device
{
    private object batteryAlertRumbleOwner;

    // Reuse the existing physical compositor, retaining the game's rumble mailbox.
    // Unlike an editor test, releasing this notification must not overwrite game feedback.
    internal virtual IDisposable TryBeginBatteryAlertRumble(byte strength)
    {
        lock (rumbleStateLock)
        {
            var normal = GetLatestRumbleState();
            if (strength == 0 || previewLightRumbleActive || previewHeavyRumbleActive ||
                normal.RumbleMotorStrengthLeftHeavySlow != 0 || normal.RumbleMotorStrengthRightLightFast != 0 ||
                DualShock4ProfileOutputDisabled || IsDisconnecting || IsRemoving) return null;
            var owner = batteryAlertRumbleOwner = new object();
            previewLightRumbleActive = previewHeavyRumbleActive = true;
            previewLightRumbleStrength = previewHeavyRumbleStrength = strength;
            previewRumbleDirty = true;
            Interlocked.Increment(ref rumbleCommandGeneration);
            return new BatteryAlertRumbleLease(() =>
            {
                lock (rumbleStateLock)
                {
                    if (!ReferenceEquals(batteryAlertRumbleOwner, owner)) return;
                    batteryAlertRumbleOwner = null;
                    previewLightRumbleActive = previewHeavyRumbleActive = false;
                    previewLightRumbleStrength = previewHeavyRumbleStrength = 0;
                    testRumble.rumbleState.RumbleMotorsExplicitlyOff =
                        testRumble.rumbleState.RumbleMotorStrengthLeftHeavySlow == 0 &&
                        testRumble.rumbleState.RumbleMotorStrengthRightLightFast == 0;
                    testRumble.dirty = true;
                    previewRumbleDirty = true;
                    Interlocked.Increment(ref rumbleCommandGeneration);
                }
            });
        }
    }
}
