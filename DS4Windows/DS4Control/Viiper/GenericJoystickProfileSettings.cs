using System;
using System.Globalization;
using System.Xml;

namespace DS4Windows;

internal sealed class GenericJoystickProfileSettings
{
    internal GenericJoystickOptions Options { get; set; } = GenericJoystickOptions.Default;
    internal bool Valid { get; set; } = true;

    internal XmlElement Write(XmlDocument document)
    {
        if (!Valid) throw new InvalidOperationException("Repair the invalid generic joystick settings before saving this profile.");
        Options.Validate();
        var node = document.CreateElement("GenericJoystick");
        void Set(string name, object value) => node.SetAttribute(name, Convert.ToString(value, CultureInfo.InvariantCulture));
        Set("Version", 1); Set("Layout", Options.Layout); Set("TriggerMode", Options.TriggerMode);
        Set("ButtonCount", Options.ButtonCount); Set("ButtonThreshold", Options.ButtonThreshold);
        Set("InvertLeftTrigger", Options.InvertLeftTrigger); Set("InvertRightTrigger", Options.InvertRightTrigger);
        Set("InvertCombinedAxis", Options.InvertCombinedAxis);
        return node;
    }

    internal static GenericJoystickProfileSettings Read(XmlNode node)
    {
        var result = new GenericJoystickProfileSettings();
        if (node == null) return result; // Existing profiles retain safe defaults.
        try
        {
            string Get(string name) => node.Attributes?[name]?.Value ?? throw new FormatException("Missing joystick field.");
            if (Get("Version") != "1") throw new FormatException("Unknown joystick profile version.");
            var options = new GenericJoystickOptions(
                Enum.Parse<GenericJoystickLayout>(Get("Layout"), false),
                Enum.Parse<GenericJoystickTriggerMode>(Get("TriggerMode"), false),
                int.Parse(Get("ButtonCount"), CultureInfo.InvariantCulture),
                int.Parse(Get("ButtonThreshold"), CultureInfo.InvariantCulture),
                bool.Parse(Get("InvertLeftTrigger")), bool.Parse(Get("InvertRightTrigger")),
                bool.Parse(Get("InvertCombinedAxis")));
            options.Validate();
            result.Options = options;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            // Keep the requested output identity; allocation must refuse this
            // profile rather than silently changing pedal resting positions.
            result.Valid = false;
        }
        return result;
    }
}
