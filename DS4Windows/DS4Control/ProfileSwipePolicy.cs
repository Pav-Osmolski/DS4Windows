using System.Threading;

namespace DS4Windows;

/// <summary>
/// Temporary does not always mean automatic: the DualSense mute button is a
/// manual selection. Only that caller grants swipe permission, at successful
/// publication. Auto Profiles and held/IPC temporary actions retain ownership.
/// </summary>
internal static class ProfileSwipePolicy
{
    private static readonly int[] manualTemporarySelections =
        new int[Global.MAX_DS4_CONTROLLER_COUNT];

    internal static bool AllowsTemporarySwipe(int slot) =>
        (uint)slot < manualTemporarySelections.Length &&
        Volatile.Read(ref manualTemporarySelections[slot]) != 0;

    internal static bool CanSwipe(int slot) =>
        (uint)slot < manualTemporarySelections.Length &&
        (!Global.useTempProfile[slot] || AllowsTemporarySwipe(slot));

    internal static void PublishTemporarySelection(int slot, bool allowSwipes)
    {
        if ((uint)slot < manualTemporarySelections.Length)
            Volatile.Write(ref manualTemporarySelections[slot], allowSwipes ? 1 : 0);
    }
}
