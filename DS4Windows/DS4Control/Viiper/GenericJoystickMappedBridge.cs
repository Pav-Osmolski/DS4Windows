namespace DS4Windows;

internal static class GenericJoystickMappedBridge
{
    internal static GenericJoystickFrame Build(in ViiperMappedInputState state, GenericJoystickOptions options)
    {
        ushort buttons = 0;
        uint sourceButtons = state.Buttons;
        void Button(uint source, int target) { if ((sourceButtons & source) != 0) buttons |= (ushort)(1 << target); }
        Button(0x20, 0); Button(0x40, 1); Button(0x10, 2); Button(0x80, 3);
        Button(0x100, 4); Button(0x200, 5); Button(0x1000, 8); Button(0x2000, 9);
        Button(0x4000, 10); Button(0x8000, 11); Button(0x10000, 12); Button(0x20000, 13);
        byte dpad = (byte)((state.DPad & 1) | ((state.DPad & 8) >> 2) |
            ((state.DPad & 2) << 1) | ((state.DPad & 4) << 1));
        return GenericJoystickMapping.Build(new GenericJoystickInput(state.LX, state.LY, state.RX, state.RY,
            state.L2, state.R2, buttons, dpad), options);
    }
}
