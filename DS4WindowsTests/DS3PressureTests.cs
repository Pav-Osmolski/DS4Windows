using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
public sealed class DS3PressureTests
{
    internal static readonly DS4Controls[] Controls = { DS4Controls.DpadUp, DS4Controls.DpadRight, DS4Controls.DpadDown, DS4Controls.DpadLeft, DS4Controls.L1, DS4Controls.R1, DS4Controls.Triangle, DS4Controls.Circle, DS4Controls.Cross, DS4Controls.Square };
    internal static readonly int[] Offsets = { 14, 15, 16, 17, 20, 21, 22, 23, 24, 25 };
    public static IEnumerable<object[]> Buttons() => Controls.Select((control, index) => new object[] { control, index });
    internal static byte[] Report(byte pressure, int offset = 1)
    {
        var report = new byte[49 + offset];
        report[2 + offset] = report[3 + offset] = 0xFF;
        report[4 + offset] = 1;
        for (int axis = 6; axis <= 9; axis++) report[axis + offset] = 128;
        for (int i = 14; i <= 25; i++) report[i + offset] = pressure;
        return report;
    }

    [TestMethod, DynamicData(nameof(Buttons))]
    public void EveryPressureLevelReachesTriggersAndBothStickDirections(DS4Controls control, int index)
    {
        foreach (int offset in new[] { 0, 1 })
        {
            var state = new DS4State(); var fields = new DS4StateFieldMapping();
            for (int value = 0; value <= 255; value++)
            {
                Assert.IsTrue(DS3InputReportParser.TryReadControls(Report((byte)value, offset), offset, state));
                Assert.IsTrue(state.DS3Pressure.TryGetValue(control, out byte actual));
                Assert.AreEqual((byte)value, actual);
                var exposed = new DS4StateExposed(state);
                fields.PopulateFieldMapping(state, exposed, null);
                Assert.AreEqual((byte)value, Mapping.GetByteMapping(0, control, state, exposed, null, fields));
                foreach (bool positive in new[] { false, true })
                {
                    var axis = Mapping.GetXYAxisMapping(0, control, state, exposed, null, fields, positive);
                    Assert.AreEqual(positive ? 128d + value * (127d / 255d) : 128d - value * (128d / 255d), axis.ProfileCoordinate, 1e-10);
                    Assert.AreEqual((short)Math.Round(value * (positive ? 32767d : -32768d) / 255d, MidpointRounding.AwayFromZero), axis.ToSigned16());
                }
                Assert.AreEqual(value > 0, fields.buttons[(int)control], "Pressure must not change digital button semantics.");
            }
            fields.buttons[(int)control] = false;
            Assert.AreEqual((byte)0, fields.GetButtonMagnitude(control), "Suppressing a digital source must also suppress its pressure mapping.");
        }
    }

    [TestMethod]
    public void ReportOrderAndReleasedDigitalBitsCannotLeakAnotherButtonsPressure()
    {
        var report = Report(0); var state = new DS4State();
        for (int i = 0; i < Controls.Length; i++) report[Offsets[i] + 1] = (byte)(i * 23 + 1);
        report[19] = 127; report[20] = 231;
        Assert.IsTrue(DS3InputReportParser.TryReadControls(report, 1, state));
        for (int i = 0; i < Controls.Length; i++)
        { Assert.IsTrue(state.DS3Pressure.TryGetValue(Controls[i], out byte value)); Assert.AreEqual((byte)(i * 23 + 1), value); }
        Assert.AreEqual((byte)127, state.L2Raw); Assert.AreEqual((byte)231, state.R2Raw);
        report[3] = report[4] = 0;
        Assert.IsTrue(DS3InputReportParser.TryReadControls(report, 1, state));
        foreach (var control in Controls)
        { state.DS3Pressure.TryGetValue(control, out byte value); Assert.AreEqual((byte)0, value); }
        Assert.AreEqual((byte)0, state.L2); Assert.AreEqual((byte)0, state.R2);
    }

    [TestMethod]
    public void TruncatedReportsClearPressureAndStateCopiesOwnTheCompleteValue()
    {
        var source = new DS4State();
        DS3InputReportParser.TryReadControls(Report(91), 1, source);
        var clone = source.Clone(); var copy = new DS4State(); var extras = new DS4State();
        source.CopyTo(copy); source.CopyExtrasTo(extras);
        var snapshot = new DS4StateOwnedSnapshot(); snapshot.Capture(source);
        foreach (var state in new[] { clone, copy, extras, snapshot.State }) Assert.AreEqual(source.DS3Pressure, state.DS3Pressure);
        for (int length = 0; length < 50; length++)
        {
            source.DS3Pressure = clone.DS3Pressure;
            Assert.IsFalse(DS3InputReportParser.TryReadControls(new byte[length], 1, source));
            Assert.IsFalse(source.DS3Pressure.IsAvailable);
        }
        Assert.IsFalse(DS3InputReportParser.TryReadControls(Report(99), -1, source));
        Assert.IsFalse(DS3InputReportParser.TryReadControls(Report(99), int.MaxValue, source));
        snapshot.Capture(new DS4State()); Assert.IsFalse(snapshot.State.DS3Pressure.IsAvailable);
    }

    [TestMethod, DynamicData(nameof(Buttons))]
    public void OtherControllersRetainDigitalFullScaleMapping(DS4Controls control, int index)
    {
        var fields = new DS4StateFieldMapping();
        foreach (bool pressed in new[] { false, true })
        {
            var source = new DS4State();
            fields.PopulateFieldMapping(source, new DS4StateExposed(source), null);
            fields.buttons[(int)control] = pressed;
            Assert.AreEqual(pressed ? (byte)255 : (byte)0, fields.GetButtonMagnitude(control));
            Assert.AreEqual(pressed ? (byte)255 : (byte)128, Mapping.GetXYAxisMapping(0, control, source, new DS4StateExposed(source), null, fields, true).LegacyValue);
            Assert.AreEqual(pressed ? (byte)0 : (byte)128, Mapping.GetXYAxisMapping(0, control, source, new DS4StateExposed(source), null, fields, false).LegacyValue);
        }
    }
}
