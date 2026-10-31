using System.Text.Json;
using DS4Windows;

if (args.Length != 1) { Console.Error.WriteLine("Usage: GenericJoystickLabPlans <output-json>"); return 1; }
var plans = new List<object>();
foreach (var layout in new[] { GenericJoystickLayout.Legacy, GenericJoystickLayout.Racing })
foreach (var mode in Enum.GetValues<GenericJoystickTriggerMode>())
{
    var options = GenericJoystickOptions.Default with { Layout = layout, TriggerMode = mode };
    var steps = new List<object>();
    void Add(string label, DS4State state)
    {
        var frame = GenericJoystickMapping.Build(state, options);
        byte[] wire = new byte[GenericJoystickFrame.Size]; frame.WriteWire(wire);
        steps.Add(new { label, wire = Convert.ToHexString(wire), axes = frame.AxisValues, buttons = frame.Buttons, hat = frame.Hat });
    }
    Add("Released controls", new());
    if (mode == GenericJoystickTriggerMode.Centred)
    {
        Add("Left stick left/up", new() { LX = 0, LY = 0 });
        Add("Left stick right/down", new() { LX = 255, LY = 255 });
        Add("Right stick left/up", new() { RX = 0, RY = 0 });
        Add("Right stick right/down", new() { RX = 255, RY = 255 });
        Add("All ten non-trigger buttons", new() { Cross = true, Circle = true, Square = true, Triangle = true, L1 = true, R1 = true, Share = true, Options = true, L3 = true, R3 = true });
        Add("PS and touchpad click", new() { PS = true, OutputTouchButton = true });
        for (int button = 0; button < 16; button++)
        {
            var frame = GenericJoystickMapping.Neutral(options) with { Buttons = (ushort)(1 << button) };
            byte[] wire = new byte[GenericJoystickFrame.Size]; frame.WriteWire(wire);
            steps.Add(new { label = $"Output button {button + 1} only", wire = Convert.ToHexString(wire), axes = frame.AxisValues, buttons = frame.Buttons, hat = frame.Hat });
        }
        Add("Hat north-east", new() { DpadUp = true, DpadRight = true });
        Add("Hat south-west", new() { DpadDown = true, DpadLeft = true });
    }
    Add("Left trigger full", new() { L2 = 255 });
    Add("Right trigger full", new() { R2 = 255 });
    Add("Both triggers full", new() { L2 = 255, R2 = 255 });
    Add("Released controls again", new());
    var neutral = GenericJoystickMapping.Neutral(options);
    plans.Add(new
    {
        name = $"{layout} layout / {mode} triggers",
        options = new { version = 1, experimental = true, buttonCount = 16, neutralAxes = neutral.AxisValues },
        neutral = new { axes = neutral.AxisValues, buttons = 0, hat = 8 },
        steps
    });
}
File.WriteAllText(Path.GetFullPath(args[0]), JsonSerializer.Serialize(plans, new JsonSerializerOptions { WriteIndented = true }));
return 0;

namespace DS4Windows
{
    // Synthetic fixture input only. This tool links the actual mapping policy
    // above, but has no physical reader or dependency on the WPF application.
    internal sealed class DS4State
    {
        public byte LX = 128, LY = 128, RX = 128, RY = 128, L2, R2;
        public bool Cross, Circle, Square, Triangle, L1, R1, Share, Options,
            L3, R3, DpadUp, DpadRight, DpadDown, DpadLeft;
        public bool PS = false, OutputTouchButton = false;
    }
}
