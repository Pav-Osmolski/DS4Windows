using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DS4Windows.InputDevices;

namespace DS4Windows;

internal sealed record ControllerInfoRow(string Name, string Value);
internal sealed record ControllerInfoSection(string Title, IReadOnlyList<ControllerInfoRow> Rows);
internal sealed record ControllerInfoPane(string Description, IReadOnlyList<ControllerInfoSection> Sections);
internal sealed record ControllerInfoSnapshot(ControllerInfoPane Physical, ControllerInfoPane Emulated);

internal sealed class ViiperControllerInfoV1
{
    public int Version { get; set; }
    public uint BusId { get; set; }
    public string DevId { get; set; }
    public string Type { get; set; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public ushort UsbRevision { get; set; }
    public string Manufacturer { get; set; }
    public string Product { get; set; }
    public string Serial { get; set; }
    public JsonElement Metadata { get; set; }
    public Dictionary<string, string> Details { get; set; }
    public byte[] FactoryReport { get; set; }
}

internal static class ControllerInfo
{
    private static ControllerInfoRow Row(string label, string value) => new(UiText.Get(label), value);
    private static string Hex(ushort value) => "0x" + value.ToString("X4", CultureInfo.InvariantCulture);
    private static string YesNo(bool value) => UiText.Get(value ? "Yes" : "No");

    internal static ControllerInfoPane Physical(DS4Device device)
    {
        var identity = new List<ControllerInfoRow>
        {
            Row("Controller", device.DisplayName),
            Row("Connection", device.ConnectionType == ConnectionType.BT ? UiText.Get("Bluetooth") : device.ConnectionType.ToString()),
        };
        if (!string.IsNullOrWhiteSpace(device.MacAddress)) identity.Add(Row("Device identifier", device.MacAddress));
        if (device.HasHidInterface && device.HidDevice != null)
        {
            identity.Add(Row("Vendor ID", Hex(unchecked((ushort)device.HidDevice.Attributes.VendorId))));
            identity.Add(Row("Product ID", Hex(unchecked((ushort)device.HidDevice.Attributes.ProductId))));
            identity.Add(Row("USB device revision", Hex(unchecked((ushort)device.HidDevice.Attributes.Version))));
        }
        var status = new List<ControllerInfoRow>();
        if (device.BatteryReadingAvailable)
        {
            status.Add(Row("Battery level", device.Battery.ToString(CultureInfo.CurrentCulture) + "%"));
            status.Add(Row("Charging", YesNo(device.Charging)));
            status.Add(Row("Charging complete", YesNo(device.BatteryChargeComplete)));
        }
        if (device.DeviceType is InputDeviceType.DS4 or InputDeviceType.DualSense)
            status.Add(Row("Headphones connected", YesNo(device.HeadphoneJackConnected)));
        status.Add(Row("Input report interval", device.Latency.ToString("0.00", CultureInfo.CurrentCulture) + " ms"));
        var sections = new List<ControllerInfoSection> { new(UiText.Get("Identity"), identity), new(UiText.Get("Status"), status) };
        var firmware = DecodeFactoryReport(device.GetControllerFactoryReport());
        if (firmware.Count != 0) sections.Add(new(UiText.Get("Factory information"), firmware));
        return new(UiText.Get("Information reported by your connected physical controller."), sections);
    }

    internal static ControllerInfoPane Emulated(ViiperControllerInfoV1 info)
    {
        if (info == null) return Unavailable(UiText.Get("Virtual output is not active."));
        if (info.Version != 1) throw new InvalidDataException("Unsupported controller info version.");
        var rows = new List<ControllerInfoRow>
        {
            Row("Controller", string.IsNullOrWhiteSpace(info.Product) ? info.Type : info.Product),
            Row("Connection", "USB"),
            Row("Vendor ID", Hex(info.VendorId)), Row("Product ID", Hex(info.ProductId)),
            Row("USB device revision", Hex(info.UsbRevision)),
        };
        if (!string.IsNullOrWhiteSpace(info.Manufacturer)) rows.Add(Row("Manufacturer", info.Manufacturer));
        if (!string.IsNullOrWhiteSpace(info.Serial)) rows.Add(Row("Serial number", info.Serial));
        if (info.Metadata.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in new[] { ("serial_number", "Serial number"), ("mac_address", "Device identifier"), ("board", "Board") })
                if (info.Metadata.TryGetProperty(pair.Item1, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && !rows.Any(r => r.Name == UiText.Get(pair.Item2)))
                    rows.Add(Row(pair.Item2, value.GetString()));
        }
        var sections = new List<ControllerInfoSection> { new(UiText.Get("Identity"), rows) };
        var firmware = DecodeFactoryReport(info.FactoryReport).ToList();
        if (info.Details != null)
            foreach (var pair in new[] { ("firmwareVersion", "Firmware version"), ("hardwareVersion", "Hardware version"), ("deviceInfo", "Device info") })
                if (info.Details.TryGetValue(pair.Item1, out string value) && !string.IsNullOrWhiteSpace(value) && value.Length <= 512)
                    firmware.Add(Row(pair.Item2, value));
        if (firmware.Count != 0) sections.Add(new(UiText.Get("Factory information"), firmware));
        else sections.Add(new(UiText.Get("Factory information"), new[] { Row("Availability", UiText.Get("This controller does not expose firmware details.")) }));
        var status = DecodeVirtualStatus(info);
        if (status.Count != 0) sections.Add(new(UiText.Get("Status"), status));
        return new(UiText.Get("Information reported by the emulated controller. Firmware dates describe emulated firmware, not DS4Windows."), sections);
    }

    internal static ControllerInfoPane Unavailable(string reason) => new(reason, Array.Empty<ControllerInfoSection>());

    private static IReadOnlyList<ControllerInfoRow> DecodeVirtualStatus(ViiperControllerInfoV1 info)
    {
        var rows = new List<ControllerInfoRow>();
        if (info.Metadata.ValueKind != JsonValueKind.Object) return rows;
        if (info.Metadata.TryGetProperty("battery_status", out var battery) && battery.ValueKind == JsonValueKind.Number && battery.TryGetInt32(out int raw) && raw is >= 0 and <= 255)
        {
            if (info.FactoryReport?.FirstOrDefault() == 0x20 && (raw & 15) <= 10 && (raw >> 4) <= 2)
            {
                rows.Add(Row("Battery level", (Math.Min(raw & 15, 10) * 10).ToString(CultureInfo.CurrentCulture) + "%"));
                rows.Add(Row("Charging", YesNo(((raw >> 4) & 15) == 1)));
                rows.Add(Row("Charging complete", YesNo(((raw >> 4) & 15) == 2)));
            }
            else if (info.FactoryReport?.FirstOrDefault() == 0xA3 && (raw & 15) <= 11)
            {
                bool externalPower = (raw & 0x10) != 0;
                int level = raw & 15;
                rows.Add(Row("Battery level", (Math.Min(level, 10) * 10).ToString(CultureInfo.CurrentCulture) + "%"));
                rows.Add(Row("Charging", YesNo(externalPower && level < 11)));
                rows.Add(Row("Charging complete", YesNo(externalPower && level == 11)));
            }
        }
        foreach (var pair in new[] { ("battery_voltage", "Battery voltage", " V"), ("temperature_celsius", "Temperature", " °C") })
            if (info.Metadata.TryGetProperty(pair.Item1, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double number) && double.IsFinite(number))
                rows.Add(Row(pair.Item2, number.ToString("0.##", CultureInfo.CurrentCulture) + pair.Item3));
        if (info.Type == "ns2pro")
        {
            if (info.Metadata.TryGetProperty("battery_level", out var level) && level.ValueKind == JsonValueKind.Number && level.TryGetInt32(out int band) && band is >= 0 and <= 9)
                rows.Add(Row("Battery band", band.ToString(CultureInfo.CurrentCulture) + " / 9"));
            foreach (var pair in new[] { ("charging", "Charging"), ("external_power", "External power") })
                if (info.Metadata.TryGetProperty(pair.Item1, out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    rows.Add(Row(pair.Item2, YesNo(flag.GetBoolean())));
            if (info.Metadata.TryGetProperty("battery_volts", out var volts) && volts.ValueKind == JsonValueKind.Number && volts.TryGetInt32(out int mv) && mv is >= 2500 and <= 5000)
                rows.Add(Row("Battery voltage", (mv / 1000.0).ToString("0.###", CultureInfo.CurrentCulture) + " V"));
        }
        return rows;
    }

    internal static IReadOnlyList<ControllerInfoRow> DecodeFactoryReport(byte[] report)
    {
        var rows = new List<ControllerInfoRow>();
        if (report == null || report.Length == 0) return rows;
        if (report[0] == 0xA3 && report.Length >= 49)
        {
            string timestamp = DateField(report, 1, 16) + " " + DateField(report, 17, 16);
            if (!ValidTimestamp(timestamp)) return rows;
            rows.Add(Row("Firmware build time", timestamp));
            rows.Add(Row("Hardware version", $"{U16(report, 33):X4}.{U16(report, 35):X4}"));
            rows.Add(Row("Firmware version", $"{U32(report, 37):X8}.{U16(report, 41):X4}"));
            rows.Add(Row("Software series", Hex(U16(report, 43))));
        }
        else if (report[0] == 0x20 && report.Length >= 64)
        {
            string timestamp = DateField(report, 1, 11) + " " + DateField(report, 12, 8);
            if (!ValidTimestamp(timestamp)) return rows;
            rows.Add(Row("Firmware build time", timestamp));
            rows.Add(Row("Hardware info", $"0x{U32(report, 24):X8}"));
            rows.Add(Row("Device info", "0x" + Convert.ToHexString(report.AsSpan(32, 12).ToArray().Reverse().ToArray())));
            rows.Add(Row("Firmware type", Hex(U16(report, 20))));
            rows.Add(Row("Software series", Hex(U16(report, 22))));
            ushort update = U16(report, 44);
            rows.Add(Row("Update version", $"{update >> 8:X}.{update & 255:X2}"));
            rows.Add(Row("Bootloader firmware version", ThreePart(U32(report, 48))));
            rows.Add(Row("Main firmware version", ThreePart(U32(report, 28))));
            uint dsp = U32(report, 52);
            rows.Add(Row("DSP firmware version", $"{dsp >> 16:X4}_{dsp & 65535:X4}"));
            rows.Add(Row("MCU DSP firmware version", ThreePart(U32(report, 56))));
        }
        return rows;
    }

    private static string DateField(byte[] data, int start, int length)
    {
        var field = data.AsSpan(start, length);
        int end = field.IndexOf((byte)0);
        if (end >= 0) field = field[..end];
        foreach (byte b in field) if (b is < 32 or > 126) return string.Empty;
        return Encoding.ASCII.GetString(field);
    }
    private static bool ValidTimestamp(string value) => DateTime.TryParseExact(value,
        new[] { "MMM dd yyyy HH:mm:ss", "MMM  d yyyy HH:mm:ss", "MMM d yyyy HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    private static ushort U16(byte[] b, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    private static uint U32(byte[] b, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));
    private static string ThreePart(uint value) => $"{value >> 24}.{(value >> 16) & 255}.{value & 65535}";

    internal static string CopyText(ControllerInfoSnapshot snapshot)
    {
        var text = new StringBuilder(UiText.Get("Controller info") + Environment.NewLine);
        foreach (var pane in new[] { (UiText.Get("Physical controller"), snapshot.Physical), (UiText.Get("Emulated controller"), snapshot.Emulated) })
        {
            text.AppendLine().AppendLine(pane.Item1).AppendLine(pane.Item2.Description);
            foreach (var section in pane.Item2.Sections)
            {
                text.AppendLine(section.Title);
                foreach (var row in section.Rows) text.AppendLine(row.Name + ": " + row.Value);
            }
        }
        return text.ToString();
    }
}
