namespace DS4Windows;

internal static class ControllerAudioRoutingPolicy
{
    internal static bool HeadphoneDetected(byte connectionStatus) => (connectionStatus & 1) != 0;
    internal static bool DualShock4HeadphoneDetected(byte powerStatus) => (powerStatus & 0x20) != 0;

    internal static bool UseHeadset(bool manualHeadset, bool followJack,
        bool headphoneConnected) => followJack ? headphoneConnected : manualHeadset;

    internal static int Selection(bool manualHeadset, bool followJack) =>
        followJack ? 2 : manualHeadset ? 1 : 0;
}
