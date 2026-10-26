using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace DS4Windows;

// Presentation metadata travels with the profile. Never retain an external
// image path or decode an unbounded image from an imported profile.
internal sealed record ProfileAppearance(string Fill = null, string Png = null)
{
    internal const string ElementName = "ProfileAppearance";
    internal const int MaximumPngBytes = 128 * 1024;
    internal static string Initial(string name) => string.IsNullOrWhiteSpace(name) ? "P" :
        StringInfo.GetNextTextElement(name.Trim()).ToUpper(CultureInfo.CurrentCulture);

    internal static string NormalizeFill(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length != 7 || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid profile icon colour.");
        return value.ToUpperInvariant();
    }

    internal Brush Brush
    {
        get
        {
            if (Fill == null) return null;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(NormalizeFill(Fill)));
            brush.Freeze(); return brush;
        }
    }

    internal BitmapSource Image => Png == null ? null : Decode(Convert.FromBase64String(Png), canonical: true);

    internal static ProfileAppearance Read(XmlElement root)
    {
        var node = root?.SelectSingleNode(ElementName);
        if (node == null) return new();
        try
        {
            string fill = NormalizeFill(node.SelectSingleNode("Fill")?.InnerText);
            string png = node.SelectSingleNode("Png")?.InnerText;
            if (string.IsNullOrEmpty(png)) png = null;
            if (png != null)
            {
                if (png.Length > MaximumPngBytes * 4 / 3 + 8) throw new InvalidDataException("Profile icon is too large.");
                _ = Decode(Convert.FromBase64String(png), canonical: true);
            }
            return new(fill, png);
        }
        catch (Exception error) when (IsImageFailure(error)) { return new(); }
    }

    internal static ProfileAppearance Import(string path, string fill)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("Choose an ICO or PNG image."));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException(UiText.Get("Choose an image smaller than 2 MB."));
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        var image = Decode(bytes, canonical: false);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream(); encoder.Save(output);
        if (output.Length > MaximumPngBytes) throw new InvalidDataException(UiText.Get("The image is too large to use as a profile icon."));
        return new(NormalizeFill(fill), Convert.ToBase64String(output.ToArray()));
    }

    private static BitmapSource Decode(byte[] bytes, bool canonical)
    {
        if (canonical && bytes.Length > MaximumPngBytes) throw new InvalidDataException("Profile icon is too large.");
        bool png = bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool ico = bytes.Length >= 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0;
        if (!png && (canonical || !ico)) throw new InvalidDataException(UiText.Get("Choose an ICO or PNG image."));
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder.Frames.Count is < 1 or > 64) throw new InvalidDataException("Invalid profile icon frames.");
        var frame = decoder.Frames.OrderByDescending(item => (long)item.PixelWidth * item.PixelHeight).First();
        int limit = canonical ? 128 : 2048;
        if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || frame.PixelWidth > limit || frame.PixelHeight > limit)
            throw new InvalidDataException(UiText.Get("Choose an image no larger than 2048 × 2048 pixels."));
        double scale = Math.Min(1, 128.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource source = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        // Materialise pixels before closing the stream; freeze for UI bindings.
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * converted.PixelHeight)]; converted.CopyPixels(pixels, stride, 0);
        var detached = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        detached.Freeze(); return detached;
    }

    internal static bool IsImageFailure(Exception error) => error is IOException or InvalidDataException or ArgumentException or FormatException or NotSupportedException or OverflowException or System.Runtime.InteropServices.COMException;

    internal static XDocument Load(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        var doc = XDocument.Load(reader);
        if (doc.Root?.Name != "DS4Windows") throw new InvalidDataException("This file is not a DS4Windows profile.");
        return doc;
    }

    internal static string Preserve(string xml, string existingPath)
    {
        if (!File.Exists(existingPath)) return xml;
        XElement existing;
        try { existing = Load(existingPath).Root.Element(ElementName); }
        catch (Exception error) when (error is XmlException or InvalidDataException) { return xml; }
        if (existing == null) return xml;
        var document = XDocument.Parse(xml);
        if (document.Root.Element(ElementName) == null) document.Root.Add(new XElement(existing));
        return document.ToString();
    }

    internal void Apply(XDocument document)
    {
        _ = NormalizeFill(Fill);
        if (Png != null) _ = Image;
        document.Root.Elements(ElementName).Remove();
        if (Fill != null || Png != null) document.Root.Add(new XElement(ElementName,
            Fill == null ? null : new XElement("Fill", Fill), Png == null ? null : new XElement("Png", Png)));
    }
}
