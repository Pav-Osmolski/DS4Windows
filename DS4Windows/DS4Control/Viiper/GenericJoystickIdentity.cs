using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DS4Windows;

internal static class GenericJoystickIdentity
{
    // Deliberately independent of profile name/options, input slot, connection
    // order and USB/IP addresses. Never put a raw MAC or HID path on the USB bus.
    internal static string Create(string profileLinkId, string devicePath)
    {
        string identity = profileLinkId?.Trim().ToUpperInvariant();
        string compact = identity?.Replace(":", "").Replace("-", "");
        if (compact?.Length == 12 && compact.All(Uri.IsHexDigit)) identity = compact;
        if (identity is "000000000000" or "FFFFFFFFFFFF") identity = null;
        string source = !string.IsNullOrWhiteSpace(identity) ? "controller:" + identity :
            !string.IsNullOrWhiteSpace(devicePath) ? "path:" + devicePath.Trim().ToUpperInvariant() : null;
        if (source == null) return null;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("DS4Windows/genericjoystick/v1/" + source));
        return Convert.ToHexString(hash.AsSpan(0, 16));
    }

    internal static string ForController(int index)
    {
        var controllers = Program.rootHub?.DS4Controllers;
        if (controllers == null || (uint)index >= controllers.Length || controllers[index] == null) return null;
        string Identity(DS4Device device) => device == null ? null : Create(device.ProfileLinkId, device.HidDevice?.DevicePath);
        string result = Identity(controllers[index]);
        if (result == null) throw new InvalidOperationException("Generic joystick output requires a controller identity or HID device path.");
        for (int other = 0; other < controllers.Length; other++)
            if (other != index && controllers[other] != null && result == Identity(controllers[other]))
                throw new InvalidOperationException("Two controllers report the same identity. Generic joystick output cannot safely distinguish them.");
        return result;
    }
}
