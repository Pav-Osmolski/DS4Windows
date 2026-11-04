namespace DS4Windows;

// Axis assignments and trigger behaviour come from the profile. These canonical
// mapping targets must not promise a particular Z/Rx/Ry role.
internal static class GenericJoystickOutputLabels
{
    internal const string DiagramHint = "Generic joystick: the diagram shows mapping targets. The diagram labels show default output assignments; the selected joystick layout and trigger mode determine stick axes and L2/R2 output. The fixed device has sixteen output buttons. The assignments below are the default; the profileâ€™s numbered button assignments can override them.";

    internal static string Get(X360Controls control) => control switch
    {
        X360Controls.A => "Button 1 (Cross)",
        X360Controls.B => "Button 2 (Circle)",
        X360Controls.X => "Button 3 (Square)",
        X360Controls.Y => "Button 4 (Triangle)",
        X360Controls.LB => "Button 5 (L1)",
        X360Controls.RB => "Button 6 (R1)",
        X360Controls.LT => "L2 (profile trigger mode)",
        X360Controls.RT => "R2 (profile trigger mode)",
        X360Controls.Back => "Button 9 (Share)",
        X360Controls.Start => "Button 10 (Options)",
        X360Controls.LS => "Button 11 (L3)",
        X360Controls.RS => "Button 12 (R3)",
        X360Controls.LXNeg => "Left stick left",
        X360Controls.LXPos => "Left stick right",
        X360Controls.LYNeg => "Left stick up",
        X360Controls.LYPos => "Left stick down",
        X360Controls.RXNeg => "Right stick left",
        X360Controls.RXPos => "Right stick right",
        X360Controls.RYNeg => "Right stick up",
        X360Controls.RYPos => "Right stick down",
        X360Controls.DpadUp => "POV up",
        X360Controls.DpadRight => "POV right",
        X360Controls.DpadDown => "POV down",
        X360Controls.DpadLeft => "POV left",
        X360Controls.Guide => "Button 13 (PS / Guide)",
        X360Controls.TouchpadClick => "Button 14 (touchpad click)",
        X360Controls.None => "Unassigned",
        _ => Global.xboxDefaultNames.TryGetValue(control, out var name) ? name : "Unassigned",
    };
}
