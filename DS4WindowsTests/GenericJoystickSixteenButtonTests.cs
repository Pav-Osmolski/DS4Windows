using System;
using System.Xml;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class GenericJoystickSixteenButtonTests
{
    [TestMethod]
    public void AllMappedSourceChannelsReachEveryNumberedOutput()
    {
        for (int source = 1; source <= 14; source++)
        for (int target = 0; target < 16; target++)
        {
            var state = new DS4State();
            switch (source)
            {
                case 1: state.Cross = true; break; case 2: state.Circle = true; break;
                case 3: state.Square = true; break; case 4: state.Triangle = true; break;
                case 5: state.L1 = true; break; case 6: state.R1 = true; break;
                case 7: state.L2 = 255; break; case 8: state.R2 = 255; break;
                case 9: state.Share = true; break; case 10: state.Options = true; break;
                case 11: state.L3 = true; break; case 12: state.R3 = true; break;
                case 13: state.PS = true; break; case 14: state.OutputTouchButton = true; break;
            }
            var options = (GenericJoystickOptions.Default with { ButtonMap = 0, TriggerMode = GenericJoystickTriggerMode.Buttons }).WithButtonSource(target, source);
            var mapped = ViiperStatePacketBuilder.BuildMappedState(state, -1, followDigitalTriggers: false);
            Assert.AreEqual((ushort)(1 << target), GenericJoystickMappedBridge.Build(mapped, options).Buttons, $"source {source} to output {target + 1}");
        }
        Assert.AreEqual((ushort)0, GenericJoystickMapping.Build(new DS4State { TouchButton = true }, GenericJoystickOptions.Default).Buttons,
            "Physical touchpad observation must not bypass mapped output-click policy.");
    }

    [TestMethod]
    public void EveryOutputCanBeAssignedAndReleasedWithoutChangingDeviceCapacity()
    {
        var state = new DS4State { Cross = true };
        for (int target = 0; target < 16; target++)
        {
            var options = GenericJoystickOptions.Default with { ButtonMap = 0 };
            options = options.WithButtonSource(target, 1);
            Assert.AreEqual((ushort)(1 << target), GenericJoystickMapping.Build(state, options).Buttons);
            var mapped = ViiperStatePacketBuilder.BuildMappedState(state, -1, followDigitalTriggers: false);
            Assert.AreEqual(GenericJoystickMapping.Build(state, options), GenericJoystickMappedBridge.Build(mapped, options));
            Assert.AreEqual((ushort)0, GenericJoystickMapping.Build(new DS4State(), options).Buttons);
            Assert.AreEqual(16, options.ButtonCount);
        }
        Assert.AreEqual((ushort)0, GenericJoystickMapping.Build(new DS4State { PS = true },
            GenericJoystickOptions.Default with { ButtonMap = 0 }).Buttons);
    }

    [TestMethod]
    public void LegacyProfileMigratesWithoutAddingGuideOrTouchpadBindings()
    {
        var xml = new XmlDocument();
        xml.LoadXml("<GenericJoystick Version='1' Layout='Legacy' TriggerMode='Buttons' ButtonCount='12' ButtonThreshold='128' InvertLeftTrigger='false' InvertRightTrigger='false' InvertCombinedAxis='false'/>");
        var loaded = GenericJoystickProfileSettings.Read(xml.DocumentElement);
        Assert.IsTrue(loaded.Valid); Assert.AreEqual(16, loaded.Options.ButtonCount);
        Assert.AreEqual((ushort)1, GenericJoystickMapping.Build(new DS4State { Cross = true, PS = true, OutputTouchButton = true }, loaded.Options).Buttons);
        var saved = loaded.Write(xml);
        Assert.AreEqual("2", saved.GetAttribute("Version")); Assert.AreEqual("16", saved.GetAttribute("ButtonCount"));
        Assert.AreEqual(loaded.Options, GenericJoystickProfileSettings.Read(saved).Options);
        saved.SetAttribute("ButtonMap", "FFFFFFFFFFFFFFFF");
        Assert.IsFalse(GenericJoystickProfileSettings.Read(saved).Valid);
        Assert.Throws<ArgumentException>(() => (GenericJoystickOptions.Default with { ButtonCount = 12 }).Validate());
    }

    [TestMethod]
    public void NumberedEditorUpdatesAllSixteenTargetsAndPersistsAssignments()
    {
        const int slot = Global.TEST_PROFILE_INDEX;
        var previous = Global.GenericJoystickSettings[slot];
        try
        {
            Global.GenericJoystickSettings[slot] = new();
            var model = new ProfileSettingsViewModel(slot);
            Assert.AreEqual(16, model.JoystickButtonAssignments.Count);
            var last = model.JoystickButtonAssignments[15];
            Assert.AreEqual("Button 16", last.Name); Assert.AreEqual(0, last.SourceIndex);
            last.SourceIndex = 6;
            var xml = new XmlDocument();
            var current = Global.GenericJoystickSettings[slot];
            var loaded = GenericJoystickProfileSettings.Read(current.Write(xml));
            Assert.AreEqual(6, loaded.Options.SourceFor(15));
            Assert.AreEqual((ushort)((1 << 5) | (1 << 15)), GenericJoystickMapping.Build(new DS4State { R1 = true }, loaded.Options).Buttons);
            last.SourceIndex = 0;
            Assert.AreEqual(0, current.Options.SourceFor(15));
        }
        finally { Global.GenericJoystickSettings[slot] = previous; }
    }
}
