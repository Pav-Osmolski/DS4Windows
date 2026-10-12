using System.Reflection;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class ShiftExtrasRuntimeTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public void ShiftExtrasActivateWithoutReplacingNormalOutputAndReleaseCleanly(
        bool normalKey, bool shiftedKey, bool normalExtrasOnly)
    {
        const int slot = Global.MAX_DS4_CONTROLLER_COUNT - 1;
        var setting = new DS4ControlSettings(DS4Controls.Cross);
        // The editor allows default output with an independent shift trigger
        // and extras. Exercise that supported state without any device or sink.
        var editor = new OutBinding
        {
            shiftBind = true,
            ShiftTrigger = 12, // L1
            outputType = OutBinding.OutType.Default,
            UseMouseSens = true,
            MouseSens = 7,
        };
        editor.WriteBind(setting);
        Assert.AreEqual(DS4ControlSettings.ActionType.Default,
            setting.shiftActionType);
        Assert.AreEqual("0,0,0,0,0,0,0,1,7", setting.shiftExtras);
        if (normalKey)
        {
            setting.actionType = DS4ControlSettings.ActionType.Key;
            setting.action.actionKey = 65;
        }
        if (shiftedKey)
        {
            setting.shiftActionType = DS4ControlSettings.ActionType.Key;
            setting.shiftAction.actionKey = 66;
        }
        if (normalExtrasOnly)
        {
            setting.extras = setting.shiftExtras;
            setting.shiftExtras = null;
        }

        var process = typeof(Mapping).GetMethod("ProcessControlSettingAction",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(process);
        var held = (DS4Controls[])typeof(Mapping).GetField("held",
            BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        DS4Controls savedHeld = held[slot];
        ButtonMouseInfo savedMouse = Global.ButtonMouseInfos[slot];
        bool savedRumble = Mapping.extrasRumbleActive[slot];
        bool savedLight = DS4LightBar.forcelight[slot];
        byte savedFlash = DS4LightBar.forcedFlash[slot];
        try
        {
            Global.ButtonMouseInfos[slot] = new ButtonMouseInfo();
            Mapping.extrasRumbleActive[slot] = false;
            held[slot] = DS4Controls.None;

            int normalSensitivity = normalExtrasOnly ? 7 : 25;
            int shiftedSensitivity = normalExtrasOnly ? 25 : 7;
            AssertFrame(controlDown: true, modifierDown: false, normalSensitivity);
            AssertFrame(controlDown: true, modifierDown: true, shiftedSensitivity);
            // Releasing only the modifier must clear extras while preserving
            // the normal output for the still-held source button.
            AssertFrame(controlDown: true, modifierDown: false, normalSensitivity);
            AssertFrame(controlDown: true, modifierDown: true, shiftedSensitivity);
            AssertFrame(controlDown: false, modifierDown: true, sensitivity: 25);
        }
        finally
        {
            held[slot] = savedHeld;
            Global.ButtonMouseInfos[slot] = savedMouse;
            Mapping.extrasRumbleActive[slot] = savedRumble;
            DS4LightBar.forcelight[slot] = savedLight;
            DS4LightBar.forcedFlash[slot] = savedFlash;
        }

        void AssertFrame(bool controlDown, bool modifierDown, int sensitivity)
        {
            var source = new DS4State { Cross = controlDown, L1 = modifierDown };
            var mapped = new DS4State(source);
            var exposed = new DS4StateExposed(source);
            var fields = new DS4StateFieldMapping();
            fields.PopulateFieldMapping(source, exposed, null);
            var outputFields = new DS4StateFieldMapping();
            outputFields.PopulateFieldMapping(source, exposed, null);
            var synthetic = new Mapping.SyntheticState();

            process.Invoke(null, new object[]
            {
                setting, slot, source, mapped, exposed, null, fields,
                outputFields, synthetic, 0.0, 0.0,
                new Mapping.AbsMouseOutput(), null,
            });

            Assert.AreEqual(sensitivity,
                Global.ButtonMouseInfos[slot].activeButtonSensitivity);
            if (controlDown && (normalKey || shiftedKey && modifierDown))
            {
                ushort expectedKey = shiftedKey && modifierDown ?
                    (ushort)66 : (ushort)65;
                Assert.AreEqual(1, synthetic.keyPresses.Count);
                Assert.IsTrue(synthetic.keyPresses.TryGetValue(expectedKey,
                    out var key));
                Assert.AreEqual(1, key.current.vkCount);
                Assert.IsFalse(outputFields.buttons[(int)DS4Controls.Cross]);
            }
            else
            {
                Assert.AreEqual(0, synthetic.keyPresses.Count);
                Assert.AreEqual(controlDown,
                    outputFields.buttons[(int)DS4Controls.Cross]);
            }
        }
    }
}
