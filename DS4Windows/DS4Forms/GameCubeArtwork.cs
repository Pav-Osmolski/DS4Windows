using System.Windows;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms;

internal static class GameCubeArtwork
{
    internal const string ResourceName = "Switch 2 GameCube Controller.png";
    internal const double Width = 1536;
    internal const double Height = 1024;

    // Source-image coordinates. Both mapping screens transform the same
    // positions into their own canvas so the diagram and targets stay aligned.
    internal static readonly Rect A = new(1100, 313, 170, 170);
    internal static readonly Geometry B = Outline(
        "M1033.5,419 A50.5,50.75 0 1 1 1033.5,520.5 A50.5,50.75 0 1 1 1033.5,419 Z");
    internal static readonly Geometry X = Outline(
        "M1318,290 C1346,289 1370,324 1377,365 C1383,405 1368,434 1340,434 " +
        "C1315,435 1304,419 1305,397 C1307,366 1300,352 1291,337 C1276,314 1293,291 1318,290 Z");
    internal static readonly Geometry Y = Outline(
        "M1069,255 C1078,232 1106,215 1137,209 C1169,203 1197,207 1208,225 " +
        "C1224,251 1206,276 1180,277 C1155,277 1137,287 1115,298 C1086,309 1061,284 1069,255 Z");
    internal static readonly Geometry L = Outline(
        "M170,214 C175,146 215,81 268,64 C312,49 359,58 389,98 L322,126 " +
        "C321,123 319,120 314,121 C298,125 277,135 258,145 C253,148 251,154 253,159 " +
        "C223,174 194,192 170,214 Z");
    internal static readonly Geometry ZL = Outline(
        "M253,159 C251,154 253,148 258,145 C277,135 298,125 314,121 " +
        "C319,120 321,123 322,126 C300,135 275,146 253,159 Z");
    internal static readonly Geometry R = Outline(
        "M1145,99 C1189,48 1245,43 1290,76 C1321,99 1339,128 1350,154 " +
        "C1294,125 1237,105 1165,93 C1155,91 1149,95 1145,99 Z");
    internal static readonly Geometry Z = Outline(
        "M1143,101 C1149,95 1155,91 1165,93 C1237,105 1294,125 1350,154 " +
        "C1361,160 1372,171 1367,185 L1359,208 C1296,170 1231,139 1176,116 C1163,111 1151,106 1143,101 Z");
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

    internal static Geometry ScaleOutline(Geometry outline, double scale, double left = 0)
    {
        Geometry scaled = outline.Clone();
        scaled.Transform = new MatrixTransform(scale, 0, 0, scale, left, 0);
        scaled.Freeze();
        return scaled;
    }

    private static Geometry Outline(string path)
    {
        Geometry outline = Geometry.Parse(path);
        outline.Freeze();
        return outline;
    }
}
