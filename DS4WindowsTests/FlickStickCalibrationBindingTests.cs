using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
public class FlickStickCalibrationBindingTests
{
    [TestMethod]
    public void CalibrationActionsAppendAfterEveryExistingOutputId()
    {
        string[] previousNames =
        {
            "None", "LXNeg", "LXPos", "LYNeg", "LYPos", "RXNeg", "RXPos",
            "RYNeg", "RYPos", "LB", "LT", "LS", "RB", "RT", "RS", "X", "Y",
            "B", "A", "DpadUp", "DpadRight", "DpadDown", "DpadLeft", "Guide",
            "Back", "Start", "TouchpadClick", "LeftMouse", "RightMouse",
            "MiddleMouse", "FourthMouse", "FifthMouse", "WUP", "WDOWN",
            "MouseUp", "MouseDown", "MouseLeft", "MouseRight", "AbsMouseUp",
            "AbsMouseDown", "AbsMouseLeft", "AbsMouseRight", "Unbound", "WLEFT", "WRIGHT",
        };
        for (int id = 0; id < previousNames.Length; id++)
            Assert.AreEqual(previousNames[id], ((X360Controls)id).ToString(),
                $"Existing output ID {id} must retain its profile meaning.");
        Assert.AreEqual(45, (byte)X360Controls.FlickStickCalibrate360LS);
        Assert.AreEqual(46, (byte)X360Controls.FlickStickCalibrate360RS);
    }

    [TestMethod]
    [DataRow(X360Controls.FlickStickCalibrate360LS, "360° turn — left stick calibration")]
    [DataRow(X360Controls.FlickStickCalibrate360RS, "360° turn — right stick calibration")]
    public void CalibrationHasMouseLabelsAndAcceptsBothProfileNameFormats(
        X360Controls action, string label)
    {
        Assert.AreEqual(label, Global.getX360ControlString(action));
        Assert.AreEqual(action, Global.getX360ControlsByName(label));
        Assert.AreEqual(action, Global.getX360ControlsByName(action.ToString()));
        foreach (OutContType output in new[]
        {
            OutContType.X360, OutContType.DS4, OutContType.ViiperX360,
            OutContType.ViiperXboxOne, OutContType.ViiperDS4,
            OutContType.ViiperDualSense, OutContType.ViiperDualSenseEdge,
            OutContType.ViiperSwitch2Pro,
        })
            Assert.AreEqual(label, Global.getX360ControlString(action, output),
                $"The calibration action must be named for output {output}.");
        Assert.IsTrue(BindAssociation.IsMouseRange(action));
        Assert.IsTrue(OutBinding.IsMouseRange(action));
    }

    [TestMethod]
    public void ProfileXmlRoundTripsRegularAndShiftedCalibrationAcrossControllerInputs()
    {
        DS4Controls[] inputs =
        {
            DS4Controls.Cross, DS4Controls.Options, DS4Controls.FnL,
            DS4Controls.SideL, DS4Controls.Switch2C,
            DS4Controls.Switch2JoyConRightPaddle1,
        };
        var source = new BackingStore();
        for (int i = 0; i < inputs.Length; i++)
        {
            DS4ControlSettings setting = source.ds4settings[0][(int)inputs[i] - 1];
            setting.UpdateSettings(false, CalibrationAction(i), string.Empty,
                DS4KeyType.None);
            setting.UpdateSettings(true, CalibrationAction(i + 1), string.Empty,
                DS4KeyType.None, trigger: 1);
        }
        // An established button mapping and a wheel mapping share the same
        // serializer and must retain their meanings beside the new actions.
        source.ds4settings[0][(int)DS4Controls.L1 - 1].UpdateSettings(
            false, X360Controls.A, string.Empty, DS4KeyType.None);
        source.ds4settings[0][(int)DS4Controls.R1 - 1].UpdateSettings(
            true, X360Controls.WRIGHT, string.Empty, DS4KeyType.None, trigger: 1);

        var dto = new ProfileDTO { DeviceIndex = 0, SerializeAppAttrs = false };
        dto.MapFrom(source);
        var serializer = new XmlSerializer(typeof(ProfileDTO),
            ProfileDTO.GetAttributeOverrides());
        string xml;
        using (var writer = new StringWriter())
        {
            serializer.Serialize(writer, dto);
            xml = writer.ToString();
        }
        var document = XDocument.Parse(xml);
        Assert.AreEqual("360° turn — left stick calibration",
            document.Root.Element("Control").Element("Button").Element("Cross").Value);
        XElement shifted = document.Root.Element("ShiftControl").Element("Button").Element("Cross");
        Assert.AreEqual("360° turn — right stick calibration", shifted.Value);
        Assert.AreEqual("1", (string)shifted.Attribute("Trigger"));

        ProfileDTO loaded;
        using (var reader = new StringReader(xml))
            loaded = (ProfileDTO)serializer.Deserialize(reader);
        var destination = new BackingStore();
        loaded.DeviceIndex = 0;
        loaded.MapTo(destination);
        for (int i = 0; i < inputs.Length; i++)
        {
            DS4ControlSettings setting = destination.ds4settings[0][(int)inputs[i] - 1];
            Assert.AreEqual(DS4ControlSettings.ActionType.Button, setting.actionType);
            Assert.AreEqual(CalibrationAction(i), setting.action.actionBtn, inputs[i].ToString());
            Assert.AreEqual(DS4ControlSettings.ActionType.Button, setting.shiftActionType);
            Assert.AreEqual(CalibrationAction(i + 1), setting.shiftAction.actionBtn, inputs[i].ToString());
            Assert.AreEqual(1, setting.shiftTrigger);
        }
        Assert.AreEqual(X360Controls.A,
            destination.ds4settings[0][(int)DS4Controls.L1 - 1].action.actionBtn);
        Assert.AreEqual(X360Controls.WRIGHT,
            destination.ds4settings[0][(int)DS4Controls.R1 - 1].shiftAction.actionBtn);
    }

    [TestMethod]
    [DataRow(X360Controls.FlickStickCalibrate360LS, false)]
    [DataRow(X360Controls.FlickStickCalibrate360RS, false)]
    [DataRow(X360Controls.FlickStickCalibrate360LS, true)]
    [DataRow(X360Controls.FlickStickCalibrate360RS, true)]
    public void SavedCalibrationBindingsRemainEditableWithoutKeyboardToggleFlags(
        X360Controls action, bool shift)
    {
        var setting = new DS4ControlSettings(DS4Controls.Cross);
        setting.UpdateSettings(false, X360Controls.B, string.Empty, DS4KeyType.None);
        setting.UpdateSettings(shift, action, string.Empty, DS4KeyType.None,
            trigger: shift ? 1 : 0);
        var editor = new BindingWindowViewModel(Global.TEST_PROFILE_INDEX, setting);
        OutBinding selected = shift ? editor.ShiftOutBind : editor.CurrentOutBind;
        editor.ActionBinding = selected;
        Assert.AreEqual(OutBinding.OutType.Button, selected.outputType);
        Assert.AreEqual(action, selected.control);
        selected.Toggle = true;
        selected.HasScanCode = true;
        editor.WriteBinds();

        Assert.AreEqual(action, (shift ? setting.shiftAction : setting.action).actionBtn);
        Assert.AreEqual(DS4KeyType.None, shift ? setting.shiftKeyType : setting.keyType,
            "A calibration mouse action cannot inherit keyboard toggle or scan-code options.");
        if (shift)
        {
            Assert.AreEqual(1, setting.shiftTrigger);
            Assert.AreEqual(X360Controls.B, setting.action.actionBtn);
        }
    }

    [TestMethod]
    public void BindingEditorDoesNotOfferFlickStickCalibrationActions()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(BindingWindowPath());
        foreach (string name in new[]
        {
            "flickStickCalibrationTab",
            "flickStickCalibrate360LSBtn",
            "flickStickCalibrate360RSBtn",
        })
        {
            Assert.IsFalse(document.Descendants().Any(element =>
                (string)element.Attribute(xaml + "Name") == name),
                "Flick-stick calibration belongs in Axis Config, outside the button remapper.");
        }
        string code = File.ReadAllText(BindingWindowPath() + ".cs");
        Assert.IsFalse(code.Contains("FlickStickCalibrate360", StringComparison.Ordinal),
            "Saved calibration output IDs must not register new remapping choices.");
        StringAssert.Contains(code, "mouseBtnMap.TryGetValue(binding.control, out Button tempBtn)");
    }

    private static X360Controls CalibrationAction(int index) => (index & 1) == 0
        ? X360Controls.FlickStickCalibrate360LS : X360Controls.FlickStickCalibrate360RS;

    private static string BindingWindowPath([CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "..", "DS4Windows", "DS4Forms", "BindingWindow.xaml");
}
