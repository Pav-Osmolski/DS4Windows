using System.Buffers.Binary;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public class GenericJoystickMappingTests
{
    private static DS4State AtRest() => new() { LX = 128, LY = 128, RX = 128, RY = 128 };

    [TestMethod]
    public void StickRangeIsMonotonicAndExactlyCentred()
    {
        Assert.AreEqual((short)-32767, GenericJoystickMapping.Stick(0));
        Assert.AreEqual((short)0, GenericJoystickMapping.Stick(128));
        Assert.AreEqual((short)32767, GenericJoystickMapping.Stick(255));
        for (int i = 1; i < 256; i++)
            Assert.IsGreaterThan(GenericJoystickMapping.Stick((byte)(i - 1)), GenericJoystickMapping.Stick((byte)i));
    }

    [TestMethod]
    public void LegacyRightStickUsesZAndRz()
    {
        var state = AtRest(); state.RX = 255; state.RY = 0; state.L2 = 255;
        var frame = GenericJoystickMapping.Build(state, GenericJoystickOptions.Default);
        Assert.AreEqual((short)32767, frame.Z);
        Assert.AreEqual((short)-32767, frame.Rz);
        Assert.AreEqual((short)32767, frame.Rx);
        Assert.AreEqual((short)0, frame.Ry);
    }

    [TestMethod]
    public void RacingPedalsUseZAndRz()
    {
        var state = AtRest(); state.RX = 255; state.RY = 0; state.R2 = 255;
        var frame = GenericJoystickMapping.Build(state, GenericJoystickOptions.Default with { Layout = GenericJoystickLayout.Racing });
        Assert.AreEqual((short)0, frame.Z);
        Assert.AreEqual((short)32767, frame.Rz);
        Assert.AreEqual((short)32767, frame.Rx);
        Assert.AreEqual((short)-32767, frame.Ry);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, -32767)]
    [DataRow(2, 0)]
    [DataRow(3, 0)]
    public void StartupNeutralMatchesTriggerMode(int modeValue, int expected)
    {
        var mode = (GenericJoystickTriggerMode)modeValue;
        var frame = GenericJoystickMapping.Neutral(GenericJoystickOptions.Default with { TriggerMode = mode });
        Assert.AreEqual((short)expected, frame.Rx);
        Assert.AreEqual((short)(mode == GenericJoystickTriggerMode.FullRange ? -32767 : 0), frame.Ry);
        Assert.AreEqual((byte)8, frame.Hat);
        Assert.AreEqual((ushort)0, frame.Buttons);
    }

    [TestMethod]
    public void EqualCombinedTriggersCancelAndOtherAxesRemainNeutral()
    {
        var state = AtRest(); var options = GenericJoystickOptions.Default with { TriggerMode = GenericJoystickTriggerMode.Combined };
        for (int i = 0; i < 256; i++)
        {
            state.L2 = state.R2 = (byte)i;
            var frame = GenericJoystickMapping.Build(state, options);
            Assert.AreEqual((short)0, frame.Rx); Assert.AreEqual((short)0, frame.Ry);
        }
        state.R2 = 0; state.L2 = 255;
        Assert.AreEqual((short)-32767, GenericJoystickMapping.Build(state, options).Rx);
        Assert.AreEqual((short)32767, GenericJoystickMapping.Build(state, options with { InvertCombinedAxis = true }).Rx);
    }

    [TestMethod]
    public void AnalogTriggersDoNotCreateDigitalFollowers()
    {
        var state = AtRest(); state.L2 = state.R2 = 255; state.L2Btn = state.R2Btn = true;
        var frame = GenericJoystickMapping.Build(state, GenericJoystickOptions.Default);
        Assert.AreEqual((ushort)0, frame.Buttons);
        var digital = GenericJoystickOptions.Default with { TriggerMode = GenericJoystickTriggerMode.Buttons, ButtonThreshold = 200 };
        state.L2 = 199; state.R2 = 200;
        frame = GenericJoystickMapping.Build(state, digital);
        Assert.AreEqual((ushort)128, frame.Buttons);
        Assert.AreEqual((short)0, frame.Rx); Assert.AreEqual((short)0, frame.Ry);
    }

    [TestMethod]
    public void TwelveButtonLayoutDoesNotLeakExtraButtons()
    {
        var state = AtRest(); state.PS = state.TouchButton = true;
        Assert.AreEqual((ushort)0, GenericJoystickMapping.Build(state, GenericJoystickOptions.Default).Buttons);
        Assert.AreEqual((ushort)0x3000, GenericJoystickMapping.Build(state, GenericJoystickOptions.Default with { ButtonCount = 16 }).Buttons);
    }

    [TestMethod]
    public void HatSupportsDiagonalsAndOppositeDirectionsCancel()
    {
        var state = AtRest(); state.DpadUp = state.DpadRight = true;
        Assert.AreEqual((byte)1, GenericJoystickMapping.Build(state, GenericJoystickOptions.Default).Hat);
        state.DpadDown = state.DpadLeft = true;
        Assert.AreEqual((byte)8, GenericJoystickMapping.Build(state, GenericJoystickOptions.Default).Hat);
    }

    [TestMethod]
    public void WireMatchesBackendNamedAxisContract()
    {
        var frame = new GenericJoystickFrame(-32767, 0, 32767, -123, 456, 789, 0x801, 8);
        byte[] wire = new byte[16]; frame.WriteWire(wire);
        Assert.AreEqual("0101800000FF7F85FFC8011503010808", Convert.ToHexString(wire));
        Assert.AreEqual((short)0, BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(3)));
        Assert.Throws<ArgumentException>(() => frame.WriteWire(new byte[15]));
    }

    [TestMethod]
    public void InvertedFullRangeTriggerStartsAtItsOtherEndpoint()
    {
        var options = GenericJoystickOptions.Default with { TriggerMode = GenericJoystickTriggerMode.FullRange, InvertLeftTrigger = true };
        Assert.AreEqual((short)32767, GenericJoystickMapping.Neutral(options).Rx);
        var state = AtRest(); state.L2 = 255;
        Assert.AreEqual((short)-32767, GenericJoystickMapping.Build(state, options).Rx);
    }

    [TestMethod]
    public void InvalidConfigurationIsRejected()
    {
        Assert.Throws<ArgumentException>(() => GenericJoystickMapping.Neutral(default));
        Assert.Throws<ArgumentException>(() => GenericJoystickMapping.Neutral(GenericJoystickOptions.Default with { ButtonThreshold = 0 }));
        Assert.Throws<ArgumentException>(() => GenericJoystickMapping.Neutral(GenericJoystickOptions.Default with { TriggerMode = GenericJoystickTriggerMode.Combined, InvertLeftTrigger = true }));
        Assert.Throws<ArgumentException>(() => new GenericJoystickFrame(short.MinValue, 0, 0, 0, 0, 0, 0, 8).WriteWire(new byte[16]));
        Assert.Throws<ArgumentException>(() => new GenericJoystickFrame(0, 0, 0, 0, 0, 0, 0, 9).WriteWire(new byte[16]));
    }
}
