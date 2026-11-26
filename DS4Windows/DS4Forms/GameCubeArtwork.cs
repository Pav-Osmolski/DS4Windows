using System.Windows;

namespace DS4WinWPF.DS4Forms;

internal static class GameCubeArtwork
{
    internal const string ResourceName = "Switch 2 GameCube Controller.png";
    internal const double Width = 1536;
    internal const double Height = 1024;

    // Source-image coordinates. Both mapping screens transform the same
    // positions into their own canvas so the diagram and targets stay aligned.
    internal static readonly Rect A = new(1100, 313, 170, 170);
    internal static readonly Rect B = new(981, 416, 107, 107);
    internal static readonly Rect X = new(1286, 287, 94, 149);
    internal static readonly Rect Y = new(1067, 207, 147, 96);
    internal static readonly Rect L = new(192, 62, 174, 81);
    internal static readonly Rect R = new(1167, 59, 145, 71);
    internal static readonly Rect Z = new(1178, 120, 183, 69);
    internal static readonly Rect ZL = new(257, 126, 62, 26);
    internal static readonly Rect Start = new(731, 361, 71, 69);
    internal static readonly Rect StickUp = new(277, 308, 116, 53);
    internal static readonly Rect StickRight = new(374, 349, 46, 88);
    internal static readonly Rect StickDown = new(277, 434, 116, 48);
    internal static readonly Rect StickLeft = new(247, 348, 50, 92);
    internal static readonly Rect CStickUp = new(957, 620, 69, 38);
    internal static readonly Rect CStickRight = new(1022, 654, 38, 66);
    internal static readonly Rect CStickDown = new(958, 713, 68, 39);
    internal static readonly Rect CStickLeft = new(930, 654, 37, 66);
    internal static readonly Rect DpadUp = new(502, 584, 57, 68);
    internal static readonly Rect DpadRight = new(553, 650, 65, 56);
    internal static readonly Rect DpadDown = new(502, 703, 57, 68);
    internal static readonly Rect DpadLeft = new(439, 650, 65, 56);
}
