namespace DS4Windows;

// Axis assignments and trigger behaviour come from the profile. These canonical
// mapping targets must not promise a particular Z/Rx/Ry role.
internal static class GenericJoystickOutputLabels
{
    internal const string DiagramHint = "Generic joystick: the diagram shows mapping targets. Buttons 1-6 and 9-12 follow the numbered labels; the selected joystick layout and trigger mode determine stick axes and L2/R2 output. Guide and touchpad click are not sent by the twelve-button device.";

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
        X360Controls.Guide => "Guide (not sent by twelve-button joystick)",
        X360Controls.TouchpadClick => "Touchpad click (not sent by twelve-button joystick)",
        X360Controls.None => "Unassigned",
        _ => Global.xboxDefaultNames.TryGetValue(control, out var name) ? name : "Unassigned",
    };
}
