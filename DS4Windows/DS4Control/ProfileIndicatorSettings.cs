using System;

namespace DS4Windows;

public enum ProfilePlayerIndicators
{
    DeviceSettings, Off, Player1, Player2, Player3, Player4, All, Battery
}

public enum ProfileMicrophoneIndicator
{
    DeviceSettings, Off, On, Pulse
}

public static class ProfileIndicatorSettings
{
    public static T Normalize<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : default;

    internal static byte? PlayerMask(ProfilePlayerIndicators mode, int battery) => Normalize(mode) switch
    {
        ProfilePlayerIndicators.Off => 0,
        ProfilePlayerIndicators.Player1 => 0x04,
        ProfilePlayerIndicators.Player2 => 0x0A,
        ProfilePlayerIndicators.Player3 => 0x15,
        ProfilePlayerIndicators.Player4 => 0x1B,
        ProfilePlayerIndicators.All => 0x1F,
        ProfilePlayerIndicators.Battery => battery >= 95 ? (byte)0x1F : battery >= 70 ? (byte)0x1B :
            battery >= 50 ? (byte)0x15 : battery >= 20 ? (byte)0x0A : battery >= 5 ? (byte)0x04 : (byte)0,
        _ => null,
    };

    internal static byte? MicByte(ProfileMicrophoneIndicator mode) => Normalize(mode) switch
    {
        ProfileMicrophoneIndicator.Off => 0,
        ProfileMicrophoneIndicator.On => 1,
        ProfileMicrophoneIndicator.Pulse => 2,
        _ => null,
    };
}
