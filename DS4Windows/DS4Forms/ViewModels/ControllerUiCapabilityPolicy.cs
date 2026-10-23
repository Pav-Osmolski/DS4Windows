using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WinWPF.DS4Forms.ViewModels
{
    internal enum ControllerMicrophoneUiStatus
    {
        Ready,
        OfflineConfiguration,
        UsbLegacyRoute,
        RequiresCompatibleController,
        RequiresBluetooth,
        RequiresPlayStationOutput,
        OutputStarting,
    }

    internal readonly struct ControllerMicrophoneUiState
    {
        internal ControllerMicrophoneUiState(ControllerMicrophoneUiStatus status,
            bool canEnable, string message)
        {
            Status = status;
            CanEnable = canEnable;
            Message = message ?? string.Empty;
        }

        internal ControllerMicrophoneUiStatus Status { get; }
        internal bool CanEnable { get; }
        internal string Message { get; }
        internal bool ShowMessage => !string.IsNullOrEmpty(Message);
        internal bool CanChange(bool currentlyEnabled) =>
            CanEnable || currentlyEnabled;
        internal bool CanAdjustLevel(bool currentlyEnabled) =>
            CanEnable && currentlyEnabled;
    }

    /// <summary>
    /// Keeps physical-controller presentation decisions in one place. Profile
    /// settings remain stored for every controller, while the frontend only
    /// advertises hardware features that the selected physical pad can use.
    /// </summary>
    internal sealed class ControllerUiCapabilities
    {
        private ControllerUiCapabilities(InputDeviceType? deviceType,
            string controllerName, string imageResourceName,
            bool isPlayStationController, bool showControllerAudioSettings,
            bool showDualSenseHardwareControls, string feedbackLabel,
            string audioHeader, string audioDescription,
            string microphoneToggleLabel,
            string microphoneDescription)
        {
            DeviceType = deviceType;
            ControllerName = controllerName;
            ImageResourceName = imageResourceName;
            IsPlayStationController = isPlayStationController;
            ShowControllerAudioSettings = showControllerAudioSettings;
            ShowDualSenseHardwareControls = showDualSenseHardwareControls;
            FeedbackLabel = feedbackLabel;
            AudioHeader = audioHeader;
            AudioDescription = audioDescription;
            MicrophoneToggleLabel = microphoneToggleLabel;
            MicrophoneDescription = microphoneDescription;
        }

        internal InputDeviceType? DeviceType { get; }
        internal string ControllerName { get; }
        internal string ImageResourceName { get; }
        internal bool HasControllerArtwork => ImageResourceName != null;
        internal bool IsPlayStationController { get; }
        internal bool IsDualShock4 => DeviceType == InputDeviceType.DS4;
        internal bool IsDualSense => DeviceType == InputDeviceType.DualSense;

        // The DS4 protocol carries an input-report interval request. Other
        // backends do not implement this command; never advertise a generic
        // host-side throttle as a change to the controller's reporting rate.
        // Offline profiles retain their existing DS4-compatible interval.
        internal bool CanConfigureBluetoothPolling =>
            DeviceType == null || IsDualShock4;
        internal bool HasRecognizedBluetoothPollingIdentity =>
            IsDualShock4 && VendorId == 0x054C &&
            ProductId is 0x05C4 or 0x09CC;
        internal string BluetoothPollingDescription
        {
            get
            {
                if (DeviceType == null)
                {
                    return global::DS4Windows.UiText.Get("Automatic uses the conservative 250 Hz (4 ms) input interval for DS4-compatible Bluetooth controllers. Turn it off to request a manual rate. Other controllers retain their managed timing; applies when the profile is saved.");
                }

                if (!IsDualShock4)
                {
                    return global::DS4Windows.UiText.Format("{0}: Bluetooth reporting is managed automatically. DS4Windows does not implement an adjustable input rate for this controller.", ControllerName);
                }

                string identity = HasRecognizedBluetoothPollingIdentity ?
                    global::DS4Windows.UiText.Get("Recognized DualShock 4 identity. The selected rate is requested from the controller; device IDs do not verify genuine hardware or confirm the accepted rate.") :
                    global::DS4Windows.UiText.Get("Unverified DS4-compatible controller. Its supported rates cannot be queried; it may ignore the selected rate.");
                string connection = ConnectionType != null &&
                    ConnectionType != DS4Windows.ConnectionType.BT ?
                    global::DS4Windows.UiText.Get(" Applies when this profile is used over Bluetooth; it does not change USB polling.") :
                    global::DS4Windows.UiText.Get(" Applies when the profile is saved.");
                return global::DS4Windows.UiText.Get("Automatic requests 250 Hz (4 ms); turn it off to choose a manual input rate. Supported rates cannot be detected automatically. ") + identity + connection;
            }
        }

        internal bool IsSwitch2 => DeviceType is
                InputDeviceType.Switch2Pro or
                InputDeviceType.Switch2JoyConLeft or
                InputDeviceType.Switch2JoyConRight or
                InputDeviceType.Switch2JoyConJoined;
        internal ConnectionType? ConnectionType { get; private set; }
        internal int? VendorId { get; private set; }
        internal int? ProductId { get; private set; }
        internal bool PhysicalIdentityKnown { get; private set; }
        internal bool ShowControllerAudioSettings { get; }
        internal bool ShowDualSenseHardwareControls { get; }
        internal bool ShowPlayStationControllerSettings =>
            ShowControllerAudioSettings || ShowDualSenseHardwareControls;
        internal bool ShowSwitch2Controls => DeviceType == null || IsSwitch2 ||
            DeviceType == InputDeviceType.SwitchPro && ProductId == 0x2069;
        internal bool ShowSwitch2StandaloneJoyConControls =>
            DeviceType == null || DeviceType is
                InputDeviceType.Switch2JoyConLeft or
                InputDeviceType.Switch2JoyConRight;
        internal bool ShowSwitch2JoyConControls => DeviceType == null ||
            DeviceType is InputDeviceType.Switch2JoyConLeft or
                InputDeviceType.Switch2JoyConRight or InputDeviceType.Switch2JoyConJoined;
        // Help for Windows' native USB audio, not an opt-in to Sony's audio
        // transport. Show it on Bluetooth too so users know to connect USB.
        internal bool ShowSwitch2UsbHeadsetHelp => DeviceType == null ||
            DeviceType == InputDeviceType.Switch2Pro ||
            DeviceType == InputDeviceType.SwitchPro &&
                VendorId == 0x057E && ProductId == 0x2069;
        // With no physical controller selected, keep the complete profile
        // surface available so offline profile editing never loses features.
        internal bool SupportsAdaptiveTriggers => DeviceType == null || IsDualSense;
        internal bool SupportsAdvancedHaptics => DeviceType == null || IsDualSense;
        internal bool SupportsMuteButton => DeviceType == null || IsDualSense;
        internal string FeedbackLabel { get; }
        internal string AudioHeader { get; }
        internal string AudioDescription { get; }
        internal string MicrophoneToggleLabel { get; }
        internal string MicrophoneDescription { get; }

        internal bool IsGenuineSonyController => PhysicalIdentityKnown &&
            ControllerAudioCapabilityPolicy.IsSupportedSonyController(
                DeviceType, VendorId, ProductId);

        internal bool SupportsControllerAudio
        {
            get
            {
                if (!PhysicalIdentityKnown)
                {
                    return DeviceType == InputDeviceType.DS4 || IsDualSense;
                }

                return ControllerAudioCapabilityPolicy.SupportsControllerAudio(
                    DeviceType, ConnectionType, VendorId, ProductId);
            }
        }

        internal bool ShowUsbDualSenseSpeakerSelector => IsDualSense &&
            PhysicalIdentityKnown && IsGenuineSonyController &&
            ConnectionType == DS4Windows.ConnectionType.USB;

        internal bool ShowLegacyMicrophoneRouting => DeviceType == null ||
            IsDualSense && PhysicalIdentityKnown && IsGenuineSonyController &&
            ConnectionType == DS4Windows.ConnectionType.USB;

        internal ControllerMicrophoneUiState GetMicrophoneUiState(
            OutContType outputType, bool activeStreamSupportsMicrophone,
            bool requireActiveStream)
        {
            if (DeviceType == null || !PhysicalIdentityKnown)
            {
                return new ControllerMicrophoneUiState(
                    ControllerMicrophoneUiStatus.OfflineConfiguration,
                    canEnable: true,
                    global::DS4Windows.UiText.Get("These settings will activate when the profile is used with a compatible PlayStation controller."));
            }

            if (!IsGenuineSonyController ||
                (!IsDualShock4 && !IsDualSense))
            {
                return new ControllerMicrophoneUiState(
                    ControllerMicrophoneUiStatus.RequiresCompatibleController,
                    canEnable: false,
                    global::DS4Windows.UiText.Get("Controller microphone routing requires a genuine Sony DualShock 4 or DualSense."));
            }

            if (IsDualSense &&
                ConnectionType == DS4Windows.ConnectionType.USB)
            {
                return new ControllerMicrophoneUiState(
                    ControllerMicrophoneUiStatus.UsbLegacyRoute,
                    canEnable: true,
                    global::DS4Windows.UiText.Get("USB DualSense microphone routing uses the capture and virtual-output endpoints in Advanced audio."));
            }

            if (ConnectionType != DS4Windows.ConnectionType.BT)
            {
                return new ControllerMicrophoneUiState(
                    ControllerMicrophoneUiStatus.RequiresBluetooth,
                    canEnable: false,
                    global::DS4Windows.UiText.Get("Direct controller microphone input requires a Bluetooth DualShock 4 or DualSense."));
            }

            if (!ControllerMicrophoneRoutePolicy
                .SupportsVirtualMicrophoneOutput(outputType))
            {
                return new ControllerMicrophoneUiState(
                    ControllerMicrophoneUiStatus.RequiresPlayStationOutput,
                    canEnable: false,
                    global::DS4Windows.UiText.Get("A VIIPER PlayStation audio interface is required to expose the controller microphone."));
            }

            if (requireActiveStream && !activeStreamSupportsMicrophone)
            {
                return new ControllerMicrophoneUiState(
                    ControllerMicrophoneUiStatus.OutputStarting,
                    canEnable: false,
                    global::DS4Windows.UiText.Get("The VIIPER microphone interface is starting. This control will become available automatically."));
            }

            return new ControllerMicrophoneUiState(
                ControllerMicrophoneUiStatus.Ready, canEnable: true,
                string.Empty);
        }

        internal bool IsMappingControlAvailable(DS4Controls control,
            bool isDualSenseEdge)
        {
            if (IsSwitch2SourceControl(control))
            {
                // Offline profiles retain the complete mapping surface. Live
                // runtimes expose only controls that their exact physical
                // model/side can produce; this never filters virtual targets.
                return IsSwitch2SourceControlAvailable(control);
            }

            if (DeviceType == null)
            {
                return true;
            }

            if (IsDualShock4)
            {
                return control != DS4Controls.Mute &&
                    control != DS4Controls.Capture &&
                    control != DS4Controls.SideL &&
                    control != DS4Controls.SideR &&
                    control != DS4Controls.FnL &&
                    control != DS4Controls.FnR &&
                    control != DS4Controls.BLP &&
                    control != DS4Controls.BRP;
            }

            if (IsDualSense)
            {
                if (control == DS4Controls.Capture ||
                    control == DS4Controls.SideL ||
                    control == DS4Controls.SideR)
                {
                    return false;
                }

                if (!isDualSenseEdge &&
                    (control == DS4Controls.FnL ||
                        control == DS4Controls.FnR ||
                        control == DS4Controls.BLP ||
                        control == DS4Controls.BRP))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool IsControllerMapListOnlyControl(DS4Controls control,
            bool isDualSenseEdge)
        {
            if (IsSwitch2SourceControl(control))
            {
                // The current Nintendo artwork has no accurate C/rail target.
                return IsMappingControlAvailable(control, isDualSenseEdge);
            }

            if (!IsDualSense || !isDualSenseEdge)
            {
                return false;
            }

            // The current controller artwork is the standard DualSense front
            // view. Edge-only controls remain fully remappable from the list,
            // but must not claim inaccurate hit targets on that diagram.
            return control == DS4Controls.FnL ||
                control == DS4Controls.FnR ||
                control == DS4Controls.BLP ||
                control == DS4Controls.BRP;
        }

        private static bool IsSwitch2SourceControl(DS4Controls control) =>
            control >= DS4Controls.Switch2C &&
            control <= DS4Controls.Switch2JoyConRightSR;

        private bool IsSwitch2SourceControlAvailable(DS4Controls control)
        {
            if (DeviceType == null)
            {
                return true;
            }

            return control switch
            {
                DS4Controls.Switch2C => DeviceType is
                    InputDeviceType.Switch2Pro or
                    InputDeviceType.Switch2JoyConRight or
                    InputDeviceType.Switch2JoyConJoined,
                DS4Controls.Switch2JoyConLeftPaddle1 or
                DS4Controls.Switch2JoyConLeftPaddle2 or
                DS4Controls.Switch2JoyConLeftSL or
                DS4Controls.Switch2JoyConLeftSR or
                DS4Controls.Switch2JoyConLeftIrSensor => DeviceType is
                    InputDeviceType.Switch2JoyConLeft or
                    InputDeviceType.Switch2JoyConJoined,
                DS4Controls.Switch2JoyConRightPaddle1 or
                DS4Controls.Switch2JoyConRightPaddle2 or
                DS4Controls.Switch2JoyConRightSL or
                DS4Controls.Switch2JoyConRightSR or
                DS4Controls.Switch2JoyConRightIrSensor => DeviceType is
                    InputDeviceType.Switch2JoyConRight or
                    InputDeviceType.Switch2JoyConJoined,
                _ => false,
            };
        }

        internal static ControllerUiCapabilities ForDevice(DS4Device device)
        {
            if (device == null)
            {
                return For(null);
            }

            int? vendorId = device.HidDevice?.Attributes?.VendorId;
            int? productId = device.HidDevice?.Attributes?.ProductId;
            return For(device.DeviceType, device.ConnectionType, vendorId,
                productId, physicalIdentityKnown: true);
        }

        internal static ControllerUiCapabilities For(InputDeviceType? deviceType)
        {
            return For(deviceType, null, null, null,
                physicalIdentityKnown: false);
        }

        internal static ControllerUiCapabilities For(InputDeviceType? deviceType,
            ConnectionType? connectionType, int? vendorId, int? productId)
        {
            return For(deviceType, connectionType, vendorId, productId,
                physicalIdentityKnown: true);
        }

        private static ControllerUiCapabilities For(InputDeviceType? deviceType,
            ConnectionType? connectionType, int? vendorId, int? productId,
            bool physicalIdentityKnown)
        {
            ControllerUiCapabilities capabilities = deviceType switch
            {
                InputDeviceType.DS4 => new ControllerUiCapabilities(
                    deviceType,
                    "DualShock 4",
                    "DualShock 4 Controller.png",
                    isPlayStationController: true,
                    showControllerAudioSettings: true,
                    showDualSenseHardwareControls: false,
                    feedbackLabel: "Rumble strength",
                    audioHeader: "DualShock 4 audio",
                    audioDescription:
                        "Bluetooth speaker output and headset-mic input through the controller's 3.5 mm jack.",
                    microphoneToggleLabel: "Enable headset microphone input",
                    microphoneDescription:
                        "DualShock 4 microphone input comes from a headset connected to the controller's 3.5 mm jack."),
                InputDeviceType.DualSense => new ControllerUiCapabilities(
                    deviceType,
                    productId == 0x0DF2 ? "DualSense Edge" : "DualSense",
                    productId == 0x0DF2
                        ? "DualSense Edge Controller.png"
                        : "DualSense Controller.png",
                    isPlayStationController: true,
                    showControllerAudioSettings: true,
                    showDualSenseHardwareControls: true,
                    feedbackLabel: "Haptic feedback strength",
                    audioHeader: "DualSense audio",
                    audioDescription:
                        "Speaker and microphone routing for the selected physical DualSense.",
                    microphoneToggleLabel: "Enable controller microphone input",
                    microphoneDescription:
                        "Uses the DualSense built-in microphone or a headset connected to the controller."),
                InputDeviceType.SwitchPro => new ControllerUiCapabilities(
                    deviceType,
                    productId == 0x2069 ? "Switch 2 Pro" : "Switch Pro",
                    "Switch 2 Pro Controller.png",
                    isPlayStationController: false,
                    showControllerAudioSettings: false,
                    showDualSenseHardwareControls: false,
                    feedbackLabel: "Rumble strength",
                    audioHeader: "Controller audio",
                    audioDescription: "Controller audio is not available on this device.",
                    microphoneToggleLabel: "Enable controller microphone input",
                    microphoneDescription: "Controller microphone input is not available on this device."),
                InputDeviceType.Switch2Pro or
                InputDeviceType.Switch2JoyConLeft or
                InputDeviceType.Switch2JoyConRight or
                InputDeviceType.Switch2JoyConJoined =>
                    new ControllerUiCapabilities(
                        deviceType,
                        deviceType == InputDeviceType.Switch2Pro ?
                            "Switch 2 Pro" :
                        deviceType == InputDeviceType.Switch2JoyConLeft ?
                            "Joy-Con 2 (L)" :
                        deviceType == InputDeviceType.Switch2JoyConRight ?
                            "Joy-Con 2 (R)" : "Joy-Con 2 (Joined)",
                        "Switch 2 Pro Controller.png",
                        isPlayStationController: false,
                        showControllerAudioSettings: false,
                        showDualSenseHardwareControls: false,
                        feedbackLabel: "HD rumble strength",
                        audioHeader: "Controller audio",
                        audioDescription:
                            "Controller audio is not available on this device.",
                        microphoneToggleLabel:
                            "Enable controller microphone input",
                        microphoneDescription:
                            "Controller microphone input is not available on this device."),
                InputDeviceType.JoyConL or
                InputDeviceType.JoyConR or
                InputDeviceType.JoyConGrip => new ControllerUiCapabilities(
                    deviceType,
                    deviceType == InputDeviceType.JoyConL ? "Joy-Con (L)" :
                        deviceType == InputDeviceType.JoyConR ? "Joy-Con (R)" :
                        "Joy-Con Grip",
                    "Switch 2 Pro Controller.png",
                    isPlayStationController: false,
                    showControllerAudioSettings: false,
                    showDualSenseHardwareControls: false,
                    feedbackLabel: "Rumble strength",
                    audioHeader: "Controller audio",
                    audioDescription: "Controller audio is not available on this device.",
                    microphoneToggleLabel: "Enable controller microphone input",
                    microphoneDescription: "Controller microphone input is not available on this device."),
                InputDeviceType.DS3 => new ControllerUiCapabilities(
                    deviceType,
                    "DualShock 3",
                    "DualShock 4 Controller.png",
                    isPlayStationController: true,
                    showControllerAudioSettings: false,
                    showDualSenseHardwareControls: false,
                    feedbackLabel: "Rumble strength",
                    audioHeader: "Controller audio",
                    audioDescription: "Controller audio is not available on DualShock 3.",
                    microphoneToggleLabel: "Enable controller microphone input",
                    microphoneDescription: "Controller microphone input is not available on DualShock 3."),
                null => new ControllerUiCapabilities(
                    null,
                    "Generic profile",
                    null,
                    isPlayStationController: false,
                    showControllerAudioSettings: true,
                    showDualSenseHardwareControls: true,
                    feedbackLabel: "Feedback strength",
                    audioHeader: "PlayStation controller audio",
                    audioDescription:
                        "Speaker and microphone settings for compatible PlayStation controllers.",
                    microphoneToggleLabel: "Enable controller microphone input",
                    microphoneDescription:
                        "These settings become active when the profile is used with a compatible PlayStation controller."),
                _ => new ControllerUiCapabilities(
                    deviceType,
                    "Controller",
                    null,
                    isPlayStationController: false,
                    showControllerAudioSettings: false,
                    showDualSenseHardwareControls: false,
                    feedbackLabel: "Rumble strength",
                    audioHeader: "PlayStation controller audio",
                    audioDescription:
                        "Select a compatible PlayStation controller to use controller audio.",
                    microphoneToggleLabel: "Enable controller microphone input",
                    microphoneDescription:
                        "Controller audio is available on compatible PlayStation controllers."),
            };

            capabilities.ConnectionType = connectionType;
            capabilities.VendorId = vendorId;
            capabilities.ProductId = productId;
            capabilities.PhysicalIdentityKnown = physicalIdentityKnown;
            return capabilities;
        }

    }
}
