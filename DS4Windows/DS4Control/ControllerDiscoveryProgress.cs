namespace DS4Windows;

internal enum ControllerDiscoveryPhase
{
    Stopped,
    CheckingComponents,
    Starting,
    Searching,
    Initialising
}

internal sealed record ControllerDiscoveryProgress(
    ControllerDiscoveryPhase Phase, string DeviceName = null);
