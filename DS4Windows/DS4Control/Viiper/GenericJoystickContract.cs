using System.IO;
using System.Text.Json;

namespace DS4Windows;

internal static class GenericJoystickContract
{
    internal static bool ReadConfigured(JsonElement args, short[] expectedNeutral = null, string expectedInstanceId = null)
    {
        bool valid = args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("contract", out var contract) && contract.ValueKind == JsonValueKind.String && contract.GetString() == "genericjoystick-v1" &&
            args.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int v) && v == 1 &&
            args.TryGetProperty("experimental", out var experimental) && experimental.ValueKind == JsonValueKind.True &&
            args.TryGetProperty("buttonCount", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int buttons) && buttons == 16;
        if (!valid || !args.TryGetProperty("neutralAxes", out var axes) || axes.ValueKind != JsonValueKind.Array || axes.GetArrayLength() != 6)
            throw new IOException("VIIPER does not support the required experimental joystick contract. Use the complete matched DirectInput preview package, including its VIIPER backend.");
        if (expectedInstanceId != null && (!args.TryGetProperty("instanceId", out var instanceId) ||
            instanceId.ValueKind != JsonValueKind.String || instanceId.GetString() != expectedInstanceId))
            throw new IOException("VIIPER did not confirm the requested joystick instance identity. Use the complete matched DirectInput preview package.");
        int index = 0;
        foreach (var axis in axes.EnumerateArray())
        {
            if (axis.ValueKind != JsonValueKind.Number || !axis.TryGetInt32(out int value) || value < -32767 || value > 32767 ||
                expectedNeutral != null && (expectedNeutral.Length != 6 || expectedNeutral[index] != value))
                throw new IOException("The backend returned incompatible joystick resting axes.");
            index++;
        }
        if (!args.TryGetProperty("inputConfigured", out var ready) || ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new IOException("The backend omitted the joystick configuration state.");
        return ready.GetBoolean();
    }
}
