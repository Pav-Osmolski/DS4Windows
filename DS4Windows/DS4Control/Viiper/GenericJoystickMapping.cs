using System;
using System.Buffers.Binary;

namespace DS4Windows
{
    // Prototype contract. Output selection remains gated to the matched portable
    // preview while physical-controller and target-game validation are pending.
    internal enum GenericJoystickLayout { Legacy, Racing }
    internal enum GenericJoystickTriggerMode { Centred, FullRange, Combined, Buttons }

    internal readonly record struct GenericJoystickOptions(
        GenericJoystickLayout Layout = GenericJoystickLayout.Legacy,
        GenericJoystickTriggerMode TriggerMode = GenericJoystickTriggerMode.Centred,
        int ButtonCount = 16,
        int ButtonThreshold = 128,
        bool InvertLeftTrigger = false,
        bool InvertRightTrigger = false,
        bool InvertCombinedAxis = false,
        ulong ButtonMap = 0x00EDCBA987654321UL)
    {
        public static GenericJoystickOptions Default => new(
            GenericJoystickLayout.Legacy, GenericJoystickTriggerMode.Centred, 16, 128);

        // Four bits per output button: 0 is unassigned, 1..14 select a mapped
        // source. Value semantics keep profile/reuse comparisons allocation-free.
        public int SourceFor(int output) => (int)((ButtonMap >> (output * 4)) & 15);
        public GenericJoystickOptions WithButtonSource(int output, int source)
        {
            if (output < 0 || output >= 16 || source < 0 || source > 14)
                throw new ArgumentOutOfRangeException();
            return this with { ButtonMap = (ButtonMap & ~(15UL << (output * 4))) | ((ulong)source << (output * 4)) };
        }

        public void Validate()
        {
            if (!Enum.IsDefined(Layout) || !Enum.IsDefined(TriggerMode) ||
                ButtonCount != 16 ||
                ButtonThreshold < 1 || ButtonThreshold > 255)
                throw new ArgumentException("Invalid experimental joystick configuration.");
            for (int output = 0; output < 16; output++)
                if (SourceFor(output) > 14) throw new ArgumentException("Unknown joystick button source.");
            if (TriggerMode == GenericJoystickTriggerMode.Combined &&
                (InvertLeftTrigger || InvertRightTrigger))
                throw new ArgumentException("Use combined-axis inversion for combined triggers.");
        }
    }

    internal readonly record struct GenericJoystickFrame(
        short X, short Y, short Z, short Rx, short Ry, short Rz,
        ushort Buttons, byte Hat)
    {
        public const int Size = 16;
        public const byte Version = 1;

        // Wire axes are signed. VIIPER biases them into the HID range; sending
        // USB report bytes directly here would put the centre in the wrong place.
        public void WriteWire(Span<byte> destination)
        {
            if (destination.Length != Size)
                throw new ArgumentException("Generic joystick frames are exactly 16 bytes.");
            if (X == short.MinValue || Y == short.MinValue || Z == short.MinValue ||
                Rx == short.MinValue || Ry == short.MinValue || Rz == short.MinValue || Hat > 8)
                throw new ArgumentException("Invalid generic joystick axis or hat value.");
            destination[0] = Version;
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(1), X);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(3), Y);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(5), Z);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(7), Rx);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(9), Ry);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(11), Rz);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(13), Buttons);
            destination[15] = Hat;
        }

        public short[] AxisValues => new[] { X, Y, Z, Rx, Ry, Rz };
    }

    internal readonly record struct GenericJoystickInput(byte LX, byte LY, byte RX, byte RY, byte L2, byte R2, ushort Buttons, byte Dpad);

    internal static class GenericJoystickMapping
    {
        public const short Maximum = 32767;

        public static GenericJoystickFrame Build(DS4State mapped,
            GenericJoystickOptions options)
        {
            ArgumentNullException.ThrowIfNull(mapped);
            ushort buttons = 0;
            if (mapped.Cross) buttons |= 1 << 0;
            if (mapped.Circle) buttons |= 1 << 1;
            if (mapped.Square) buttons |= 1 << 2;
            if (mapped.Triangle) buttons |= 1 << 3;
            if (mapped.L1) buttons |= 1 << 4;
            if (mapped.R1) buttons |= 1 << 5;
            if (mapped.Share) buttons |= 1 << 8;
            if (mapped.Options) buttons |= 1 << 9;
            if (mapped.L3) buttons |= 1 << 10;
            if (mapped.R3) buttons |= 1 << 11;
            if (mapped.PS) buttons |= 1 << 12;
            if (mapped.OutputTouchButton) buttons |= 1 << 13;
            byte dpad = (byte)((mapped.DpadUp ? 1 : 0) | (mapped.DpadRight ? 2 : 0) |
                (mapped.DpadDown ? 4 : 0) | (mapped.DpadLeft ? 8 : 0));
            return Build(new GenericJoystickInput(mapped.LX, mapped.LY, mapped.RX, mapped.RY,
                mapped.L2, mapped.R2, buttons, dpad), options);
        }

        internal static GenericJoystickFrame Build(GenericJoystickInput mapped, GenericJoystickOptions options)
        {
            options.Validate();
            short lx = Stick(mapped.LX), ly = Stick(mapped.LY);
            short rx = Stick(mapped.RX), ry = Stick(mapped.RY);
            short lt = 0, rt = 0;
            switch (options.TriggerMode)
            {
                case GenericJoystickTriggerMode.Centred:
                    lt = Trigger(mapped.L2, false, options.InvertLeftTrigger);
                    rt = Trigger(mapped.R2, false, options.InvertRightTrigger);
                    break;
                case GenericJoystickTriggerMode.FullRange:
                    lt = Trigger(mapped.L2, true, options.InvertLeftTrigger);
                    rt = Trigger(mapped.R2, true, options.InvertRightTrigger);
                    break;
                case GenericJoystickTriggerMode.Combined:
                    lt = (short)((mapped.R2 - mapped.L2) * Maximum / 255);
                    if (options.InvertCombinedAxis) lt = (short)-lt;
                    break;
            }

            ushort buttons = (ushort)(mapped.Buttons & 0x3F3F);
            // Do not inherit Sony's digital trigger followers in analog modes.
            if (options.TriggerMode == GenericJoystickTriggerMode.Buttons)
            {
                if (mapped.L2 >= options.ButtonThreshold) buttons |= 1 << 6;
                if (mapped.R2 >= options.ButtonThreshold) buttons |= 1 << 7;
            }
            ushort assigned = 0;
            for (int output = 0; output < 16; output++)
            {
                int source = options.SourceFor(output);
                if (source != 0 && (buttons & (1 << (source - 1))) != 0)
                    assigned |= (ushort)(1 << output);
            }
            buttons = assigned;
            byte hat = Hat((mapped.Dpad & 1) != 0, (mapped.Dpad & 2) != 0, (mapped.Dpad & 4) != 0, (mapped.Dpad & 8) != 0);
            return options.Layout == GenericJoystickLayout.Legacy
                ? new GenericJoystickFrame(lx, ly, rx, lt, rt, ry, buttons, hat)
                : new GenericJoystickFrame(lx, ly, lt, rx, ry, rt, buttons, hat);
        }

        public static GenericJoystickFrame Neutral(GenericJoystickOptions options)
        {
            return Build(new DS4State { LX = 128, LY = 128, RX = 128, RY = 128 }, options);
        }

        internal static short Stick(byte value) => value >= 128
            ? (short)((value - 128) * Maximum / 127)
            : (short)((value - 128) * Maximum / 128);

        private static short Trigger(byte value, bool fullRange, bool invert)
        {
            int result = fullRange ? value * (2 * Maximum) / 255 - Maximum
                : value * Maximum / 255;
            return (short)(invert ? -result : result);
        }

        private static byte Hat(bool up, bool right, bool down, bool left)
        {
            int x = (right ? 1 : 0) - (left ? 1 : 0);
            int y = (down ? 1 : 0) - (up ? 1 : 0);
            return (x, y) switch
            {
                (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3,
                (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7,
                _ => 8,
            };
        }
    }
}
