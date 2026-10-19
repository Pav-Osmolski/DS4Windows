using System;

namespace DS4Windows;

public enum SidebarIconChoice
{
    BlueTile = 0,
    MidnightCircle = 1,
    BlueFrame = 2,
    Controller = 3,
    AngularBadge = 4,
}

internal static class SidebarIconPolicy
{
    internal static SidebarIconChoice Normalize(SidebarIconChoice value) =>
        Enum.IsDefined(value) ? value : SidebarIconChoice.BlueTile;

    internal static SidebarIconChoice Parse(string value) =>
        Enum.TryParse(value, out SidebarIconChoice choice)
            ? Normalize(choice) : SidebarIconChoice.BlueTile;
}
