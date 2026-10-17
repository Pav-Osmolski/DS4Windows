using System.IO;
using System.Xml.Serialization;
using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public class ProfileIndicatorTests
{
    [TestMethod]
    [DataRow(ConnectionType.USB, 1)]
    [DataRow(ConnectionType.BT, 2)]
    public void PhysicalReportReapplyClearsLightsEvenWhenOffWasAlreadySelected(ConnectionType connection, int offset)
    {
        var hid = (HidDevice)RuntimeHelpers.GetUninitializedObject(typeof(HidDevice));
        Set(hid, typeof(HidDevice), "_deviceAttributes", new HidDeviceAttributes(
            new NativeMethods.HIDD_ATTRIBUTES { VendorID = 0x054C, ProductID = 0x0CE6 }));
        var device = new DualSenseDevice(hid, "Indicator output regression");
        Set(device, typeof(DS4Device), "conType", connection);
        byte[] report = new byte[connection == ConnectionType.USB ? 64 : 78];
        Set(device, typeof(DS4Device), "outputReport", report);
        var prepare = typeof(DualSenseDevice).GetMethod("PrepareOutReport", BindingFlags.Instance | BindingFlags.NonPublic);
        device.ApplyProfileIndicators(ProfilePlayerIndicators.Off, ProfileMicrophoneIndicator.Off);
        prepare.Invoke(device, null);
        Set(device, typeof(DualSenseDevice), "outputDirty", false);
        report[offset + 43] = 0x1F;
        report[offset + 8] = 2;
        device.ApplyProfileIndicators(ProfilePlayerIndicators.Off, ProfileMicrophoneIndicator.Off, reapply: true);
        prepare.Invoke(device, null);
        Assert.IsTrue((bool)typeof(DualSenseDevice).GetField("outputDirty", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(device));
        Assert.AreEqual((byte)0, report[offset + 43]);
        Assert.AreEqual((byte)0, report[offset + 8]);
        Assert.AreEqual(0x11, report[offset + 1] & 0x11, "Off needs explicit player/mic validity, not absent fields.");
    }

    private static void Set(object target, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

    [TestMethod]
    [DataRow(100, 0x1F)]
    [DataRow(80, 0x1B)]
    [DataRow(60, 0x15)]
    [DataRow(30, 0x0A)]
    [DataRow(10, 0x04)]
    [DataRow(0, 0)]
    public void BatteryPatternsUseFiveToZeroLights(int battery, int expected) =>
        Assert.AreEqual((byte)expected, ProfileIndicatorSettings.PlayerMask(ProfilePlayerIndicators.Battery, battery).Value);

    [TestMethod]
    public void NativeCompositorKeepsMuteButtonPriorityAndOriginalGameState()
    {
        var state = DualSensePhysicalOutputSnapshot.Default with
        {
            ProfilePlayerLedMask = 0, ProfileMicLedByte = 0,
            MicrophoneMuteOverride = true, MicrophoneMuted = true,
            NativeGameLightbarOwnershipReleased = false,
        };
        byte[] native = new byte[48];
        native[0] = 2; native[2] = 0x11; native[9] = 2; native[44] = 0x1F;
        byte[] before = (byte[])native.Clone();
        byte[] output = new byte[48];
        DualSenseDevice.PrepareUsbNativeGameReportWithLocalOverridesInto(native, output, state);
        Assert.AreEqual((byte)0, output[44]);
        Assert.AreEqual((byte)1, output[9], "Mute controls must still communicate microphone mute status.");
        Assert.AreEqual(0x10, output[10] & 0x10);
        CollectionAssert.AreEqual(before, native, "Profile indicators must not overwrite the saved native underlay.");
        state = state with { ProfilePlayerLedMask = null, ProfileMicLedByte = null, MicrophoneMuteOverride = false };
        DualSenseDevice.PrepareUsbNativeGameReportWithLocalOverridesInto(native, output, state);
        Assert.AreEqual((byte)0x1F, output[44], "Returning to device settings must reveal native player indicators again.");
    }

    [TestMethod]
    public void OldProfileReturnsToDeviceSettingsAfterExplicitOverrides()
    {
        var store = new BackingStore();
        store.lightbarSettingInfo[0].PlayerIndicators = ProfilePlayerIndicators.Off;
        store.lightbarSettingInfo[0].MicrophoneIndicator = ProfileMicrophoneIndicator.On;
        var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
        using var reader = new StringReader("<DS4Windows><LightbarMode>DS4Win</LightbarMode></DS4Windows>");
        var profile = (ProfileDTO)serializer.Deserialize(reader);
        profile.DeviceIndex = 0;
        profile.MapTo(store);
        Assert.AreEqual(ProfilePlayerIndicators.DeviceSettings, store.lightbarSettingInfo[0].PlayerIndicators);
        Assert.AreEqual(ProfileMicrophoneIndicator.DeviceSettings, store.lightbarSettingInfo[0].MicrophoneIndicator);
    }

    [TestMethod]
    public void ProfileRoundTripPreservesIndependentIndicatorSettings()
    {
        var store = new BackingStore();
        store.lightbarSettingInfo[0].PlayerIndicators = ProfilePlayerIndicators.Off;
        store.lightbarSettingInfo[0].MicrophoneIndicator = ProfileMicrophoneIndicator.Pulse;
        store.lightbarSettingInfo[0].mode = LightbarMode.Passthru;
        var profile = new ProfileDTO { DeviceIndex = 0 };
        profile.MapFrom(store);
        var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
        using var writer = new StringWriter();
        serializer.Serialize(writer, profile);
        using var reader = new StringReader(writer.ToString());
        var copy = (ProfileDTO)serializer.Deserialize(reader);
        copy.DeviceIndex = 0;
        var destination = new BackingStore();
        copy.MapTo(destination);
        Assert.AreEqual(ProfilePlayerIndicators.Off, destination.lightbarSettingInfo[0].PlayerIndicators);
        Assert.AreEqual(ProfileMicrophoneIndicator.Pulse, destination.lightbarSettingInfo[0].MicrophoneIndicator);
        Assert.AreEqual(LightbarMode.Passthru, destination.lightbarSettingInfo[0].mode);
    }

    [TestMethod]
    public void InvalidValuesInMemoryFallBackToDeviceSettings()
    {
        Assert.IsNull(ProfileIndicatorSettings.PlayerMask((ProfilePlayerIndicators)999, 100));
        Assert.IsNull(ProfileIndicatorSettings.MicByte((ProfileMicrophoneIndicator)999));
    }

    [TestMethod]
    public void ReapplyingUnchangedOffPublishesAgainWithoutChangingOtherOutput()
    {
        var mailbox = new DualSensePhysicalOutputStateMailbox();
        mailbox.SetActivePlayerLedMask(0x1F);
        mailbox.SetProfileIndicators(0, 0, false);
        long version = 0;
        Assert.IsTrue(mailbox.TryClaim(ref version, out var before));
        Assert.IsFalse(mailbox.SetProfileIndicators(0, 0, false));
        Assert.IsFalse(mailbox.TryClaim(ref version, out _));
        Assert.IsTrue(mailbox.SetProfileIndicators(0, 0, true));
        Assert.IsTrue(mailbox.TryClaim(ref version, out var after));
        Assert.AreEqual(before.IndicatorReapplyRevision + 1, after.IndicatorReapplyRevision);
        Assert.AreEqual(before, after with { IndicatorReapplyRevision = before.IndicatorReapplyRevision });
        Assert.AreEqual((byte)0, after.EffectivePlayerLedMask);
        mailbox.SetProfileIndicators(null, null, false);
        Assert.AreEqual((byte)0x1F, mailbox.ReadLatest().EffectivePlayerLedMask);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(13)]
    public void ExplicitOffClearsNativePlayerAndMicWithValidityFlags(int offset)
    {
        var state = DualSensePhysicalOutputSnapshot.Default with
        {
            ProfilePlayerLedMask = 0, ProfileMicLedByte = 0,
            NativeGameLightbarOwnershipReleased = false,
        };
        byte[] report = Enumerable.Range(0, offset + 47).Select(i => (byte)i).ToArray();
        report[offset + 1] = 0x08; // A native release must not cancel explicit profile indicators.
        byte[] before = (byte[])report.Clone();
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, offset, state);
        Assert.AreEqual((byte)0x11, report[offset + 1]);
        Assert.AreEqual((byte)0, report[offset + 43]);
        Assert.AreEqual((byte)0, report[offset + 8]);
        for (int i = 0; i < report.Length; i++)
            if (i != offset + 1 && i != offset + 43 && i != offset + 8)
                Assert.AreEqual(before[i], report[i], $"Unrelated field {i} changed.");
    }

    [TestMethod]
    public void DsxIndicatorsRetainPriorityAndReleaseReturnsToProfileOff()
    {
        var state = DualSensePhysicalOutputSnapshot.Default with
        {
            ProfilePlayerLedMask = 0, ProfileMicLedByte = 0,
            DsxOverlay = new DualSenseDsxOverlay(new object(), MicLed: 2, PlayerLeds: 0x1F),
            DsxNativeState = new DualSenseDsxOverlay(MicLed: 1, PlayerLeds: 0x04),
            NativeGameLightbarOwnershipReleased = false,
        };
        byte[] report = new byte[48];
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, 1, state);
        Assert.AreEqual((byte)0x1F, report[44]);
        Assert.AreEqual((byte)2, report[9]);
        state = state with { DsxOverlay = default };
        DualSenseDevice.ApplyDsxOverlayToNativeReport(report, 1, state,
            DualSenseDsxOverlay.PlayerField | DualSenseDsxOverlay.MicField);
        Assert.AreEqual((byte)0, report[44]);
        Assert.AreEqual((byte)0, report[9]);
    }
}
