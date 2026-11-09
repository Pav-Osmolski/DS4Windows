using System;
using System.Windows;
using System.Windows.Controls;
using DS4Windows;
using System.Linq;

namespace DS4WinWPF.DS4Forms
{
    public partial class ProfileEditor
    {
        private CheckBox axisReadingsToggle;
        private ContentControl axisReadingsHost;
        private AxialStickUserControl axialLSStickControl;
        private Label lsOuterBindLb;
        private AxialStickUserControl axialRSStickControl;
        private Label rsOuterBindLb;
        private Grid axisContentArea;
        private GroupBox colorGB;
        private GroupBox emptyColorGB;
        private CheckBox colorByBatteryPerCk;
        private Xceed.Wpf.Toolkit.DoubleUpDown frictionUD;
        private Button touchDisInvertBtn;
        private ContentControl touchContentControl1;
        private Button swipeUpBtn;
        private Label swipeUpLb;
        private Button swipeDownBtn;
        private Label swipeDownLb;
        private Button swipeLeftBtn;
        private Label swipeLeftLb;
        private Button swipeRightBtn;
        private Label swipeRightLb;
        private ContentControl touchContentControl2;
        private ContentControl touchContentControl4;
        private ContentControl touchContentControl3;
        private ComboBox gyroOutModeCombo;
        private Button gyroZNBtn;
        private Label gyroZNLb;
        private Button gyroZPBtn;
        private Label gyroZPLb;
        private Button gyroXPBtn;
        private Label gyroXLb;
        private Button gyroXNBtn;
        private Label gyroXNLb;
        private Button gyroControlsTrigBtn;
        private Button gyroMouseTrigBtn;
        private Button gyroMouseStickTrigBtn;
        private Label gyroSwipeUpLb;
        private Label gyroSwipeDownLb;
        private Label gyroSwipeLeftLb;
        private Label gyroSwipeRightLb;
        private Button gyroSwipeTrigBtn;
        private AudioHapticsControl profileAudioHapticsControl;
        private TriggerLabControl profileTriggerLabControl;
        private Expander switch2MotionCard;
        private ComboBox Switch2SecondActionControlCombo;
        private Button Switch2MagnetometerCalibrationButton;
        private Button Switch2MagnetometerCalibrationCancelButton;
        private TextBlock Switch2MagnetometerCalibrationStatus;
        private ComboBox outConTypeCombo;
        private TextBlock outputControllerHintText;
        private StackPanel switch2ViiperLegendPanel;
        private Button heavyRumbleTestBtn;
        private Button lightRumbleTestBtn;
        private Xceed.Wpf.Toolkit.IntegerUpDown useControllerUD;

        private DeferredProfilePage[] DeferredPages => new[] { axisPage, lightbarPage, touchpadPage, gyroPage, audioHapticsPage, triggerLabPage, nintendoPage, advancedPage };

        private void InitialiseAxisPage()
        {
            RefreshAxisBindings();
            AssignStickOuterBindAssociation();
        }

        private void RefreshAxisBindings()
        {
            if (axialLSStickControl.AxialVM != null)
            {
                axialLSStickControl.AxialVM.DeadZoneXChanged -= UpdateReadingsLsDeadZoneX;
                axialLSStickControl.AxialVM.DeadZoneYChanged -= UpdateReadingsLsDeadZoneY;
                axialRSStickControl.AxialVM.DeadZoneXChanged -= UpdateReadingsRsDeadZoneX;
                axialRSStickControl.AxialVM.DeadZoneYChanged -= UpdateReadingsRsDeadZoneY;
            }
            axialLSStickControl.UseDevice(Global.LSModInfo[deviceNum]);
            axialRSStickControl.UseDevice(Global.RSModInfo[deviceNum]);
            axialLSStickControl.AxialVM.DeadZoneXChanged += UpdateReadingsLsDeadZoneX;
            axialLSStickControl.AxialVM.DeadZoneYChanged += UpdateReadingsLsDeadZoneY;
            axialRSStickControl.AxialVM.DeadZoneXChanged += UpdateReadingsRsDeadZoneX;
            axialRSStickControl.AxialVM.DeadZoneYChanged += UpdateReadingsRsDeadZoneY;
        }

        private void InitialiseLightbarPage() => ColorByBatteryPerCheck();

        private void InitialiseTouchpadPage()
        {
            touchButtonUC = new TouchButtonUserControl(deviceNum);
            TouchpadButtonControlDisplaySetup();
            AssignSwipeAssociation();
            PopulateHoverIndexes();
            PopulateReverseHoverIndexes();
            profileSettingsVM.PopulateTouchDisInver(touchDisInvertBtn.ContextMenu);
        }

        private void InitialiseGyroPage()
        {
            PopulateGyroActionsTriggersMenu();
            AssignTiltAssociation();
            AssignGyroSwipeAssociation();
            PopulateHoverIndexes();
            PopulateReverseHoverIndexes();
            RefreshGyroMenus();
            gyroOutModeCombo.SelectionChanged += GyroOutModeCombo_SelectionChanged;
        }

        private void InitialiseAudioHapticsPage() => profileAudioHapticsControl.SetDevice(deviceNum);
        private void InitialiseTriggerLabPage() => profileTriggerLabControl.SetDevice(deviceNum, triggerPreviewDeviceIndex);

        private void InitialiseNintendoPage()
        {
            Switch2SecondActionControlCombo.ItemsSource = mappingListVM.Mappings
                .Where(control => control.IsAvailableOnPhysicalController).ToArray();
            Switch2SecondActionControlCombo.SelectedIndex = 0;
        }

        private void InitialiseAdvancedPage()
        {
            profileSettingsVM.StartEditorDiscovery();
            UpdateOutputControllerHint(profileSettingsVM.TempConType);
            useControllerUD.Value = mappingReadingsDevice >= 0 ? mappingReadingsDevice + 1 : 1;
            outConTypeCombo.SelectionChanged += OutConTypeCombo_SelectionChanged;
        }

        private void RefreshGyroMenus()
        {
            profileSettingsVM.PopulateGyroMouseTrig(gyroMouseTrigBtn.ContextMenu);
            profileSettingsVM.PopulateGyroMouseStickTrig(gyroMouseStickTrigBtn.ContextMenu);
            profileSettingsVM.PopulateGyroSwipeTrig(gyroSwipeTrigBtn.ContextMenu);
            profileSettingsVM.PopulateGyroControlsTrig(gyroControlsTrigBtn.ContextMenu);
        }

        private void RefreshMaterializedPages()
        {
            if (axialLSStickControl != null) RefreshAxisBindings();
            if (touchButtonUC != null)
            {
                touchButtonUC.RefreshDataContext();
                TouchpadButtonControlDisplaySetup();
                profileSettingsVM.PopulateTouchDisInver(touchDisInvertBtn.ContextMenu);
            }
            if (gyroOutModeCombo != null) RefreshGyroMenus();
            profileAudioHapticsControl?.SetDevice(deviceNum);
            profileTriggerLabControl?.SetDevice(deviceNum, triggerPreviewDeviceIndex);
            UpdateOutputControllerHint(profileSettingsVM.TempConType);
            ColorByBatteryPerCheck();
        }

        private void RefreshReadingsValues()
        {
            var left = Global.LSModInfo[deviceNum];
            var right = Global.RSModInfo[deviceNum];
            conReadingsUserCon.LsDeadX = left.deadzoneType == StickDeadZoneInfo.DeadZoneType.Axial ? Math.Round(left.xAxisDeadInfo.deadZone / 127d, 2) : profileSettingsVM.LSDeadZone;
            conReadingsUserCon.LsDeadY = left.deadzoneType == StickDeadZoneInfo.DeadZoneType.Axial ? Math.Round(left.yAxisDeadInfo.deadZone / 127d, 2) : profileSettingsVM.LSDeadZone;
            conReadingsUserCon.RsDeadX = right.deadzoneType == StickDeadZoneInfo.DeadZoneType.Axial ? Math.Round(right.xAxisDeadInfo.deadZone / 127d, 2) : profileSettingsVM.RSDeadZone;
            conReadingsUserCon.RsDeadY = right.deadzoneType == StickDeadZoneInfo.DeadZoneType.Axial ? Math.Round(right.yAxisDeadInfo.deadZone / 127d, 2) : profileSettingsVM.RSDeadZone;
            conReadingsUserCon.L2Dead = profileSettingsVM.L2DeadZone;
            conReadingsUserCon.R2Dead = profileSettingsVM.R2DeadZone;
            conReadingsUserCon.SixAxisXDead = profileSettingsVM.SXDeadZone;
            conReadingsUserCon.SixAxisZDead = profileSettingsVM.SZDeadZone;
            conReadingsUserCon.LsDriftX = profileSettingsVM.LeftStickDriftXAxis;
            conReadingsUserCon.LsDriftY = profileSettingsVM.LeftStickDriftYAxis;
            conReadingsUserCon.RsDriftX = profileSettingsVM.RightStickDriftXAxis;
            conReadingsUserCon.RsDriftY = profileSettingsVM.RightStickDriftYAxis;
        }

        private void AxisPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            axisReadingsToggle = e.Find<CheckBox>("axisReadingsToggle");
            axisReadingsHost = e.Find<ContentControl>("axisReadingsHost");
            axialLSStickControl = e.Find<AxialStickUserControl>("axialLSStickControl");
            lsOuterBindLb = e.Find<Label>("lsOuterBindLb");
            axialRSStickControl = e.Find<AxialStickUserControl>("axialRSStickControl");
            rsOuterBindLb = e.Find<Label>("rsOuterBindLb");
            axisContentArea = e.Find<Grid>("axisContentArea");
            InitialiseAxisPage();
        }

        private void LightbarPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            colorGB = e.Find<GroupBox>("colorGB");
            colorGB.IsVisibleChanged += MainLightbarColorGroup_IsVisibleChanged;
            emptyColorGB = e.Find<GroupBox>("emptyColorGB");
            colorByBatteryPerCk = e.Find<CheckBox>("colorByBatteryPerCk");
            InitialiseLightbarPage();
        }

        private void TouchpadPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            frictionUD = e.Find<Xceed.Wpf.Toolkit.DoubleUpDown>("frictionUD");
            touchDisInvertBtn = e.Find<Button>("touchDisInvertBtn");
            touchContentControl1 = e.Find<ContentControl>("touchContentControl1");
            swipeUpBtn = e.Find<Button>("swipeUpBtn");
            swipeUpLb = e.Find<Label>("swipeUpLb");
            swipeDownBtn = e.Find<Button>("swipeDownBtn");
            swipeDownLb = e.Find<Label>("swipeDownLb");
            swipeLeftBtn = e.Find<Button>("swipeLeftBtn");
            swipeLeftLb = e.Find<Label>("swipeLeftLb");
            swipeRightBtn = e.Find<Button>("swipeRightBtn");
            swipeRightLb = e.Find<Label>("swipeRightLb");
            touchContentControl2 = e.Find<ContentControl>("touchContentControl2");
            touchContentControl4 = e.Find<ContentControl>("touchContentControl4");
            touchContentControl3 = e.Find<ContentControl>("touchContentControl3");
            InitialiseTouchpadPage();
        }

        private void GyroPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            gyroOutModeCombo = e.Find<ComboBox>("gyroOutModeCombo");
            gyroZNBtn = e.Find<Button>("gyroZNBtn");
            gyroZNLb = e.Find<Label>("gyroZNLb");
            gyroZPBtn = e.Find<Button>("gyroZPBtn");
            gyroZPLb = e.Find<Label>("gyroZPLb");
            gyroXPBtn = e.Find<Button>("gyroXPBtn");
            gyroXLb = e.Find<Label>("gyroXLb");
            gyroXNBtn = e.Find<Button>("gyroXNBtn");
            gyroXNLb = e.Find<Label>("gyroXNLb");
            gyroControlsTrigBtn = e.Find<Button>("gyroControlsTrigBtn");
            gyroMouseTrigBtn = e.Find<Button>("gyroMouseTrigBtn");
            gyroMouseStickTrigBtn = e.Find<Button>("gyroMouseStickTrigBtn");
            gyroSwipeUpLb = e.Find<Label>("gyroSwipeUpLb");
            gyroSwipeDownLb = e.Find<Label>("gyroSwipeDownLb");
            gyroSwipeLeftLb = e.Find<Label>("gyroSwipeLeftLb");
            gyroSwipeRightLb = e.Find<Label>("gyroSwipeRightLb");
            gyroSwipeTrigBtn = e.Find<Button>("gyroSwipeTrigBtn");
            InitialiseGyroPage();
        }

        private void AudioHapticsPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            profileAudioHapticsControl = e.Find<AudioHapticsControl>("profileAudioHapticsControl");
            InitialiseAudioHapticsPage();
        }

        private void TriggerLabPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            profileTriggerLabControl = e.Find<TriggerLabControl>("profileTriggerLabControl");
            InitialiseTriggerLabPage();
        }

        private void NintendoPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            switch2MotionCard = e.Find<Expander>("switch2MotionCard");
            Switch2SecondActionControlCombo = e.Find<ComboBox>("Switch2SecondActionControlCombo");
            Switch2MagnetometerCalibrationButton = e.Find<Button>("Switch2MagnetometerCalibrationButton");
            Switch2MagnetometerCalibrationCancelButton = e.Find<Button>("Switch2MagnetometerCalibrationCancelButton");
            Switch2MagnetometerCalibrationStatus = e.Find<TextBlock>("Switch2MagnetometerCalibrationStatus");
            InitialiseNintendoPage();
        }

        private void AdvancedPage_Materializing(object sender, ProfilePageMaterializingEventArgs e)
        {
            outConTypeCombo = e.Find<ComboBox>("outConTypeCombo");
            outputControllerHintText = e.Find<TextBlock>("outputControllerHintText");
            switch2ViiperLegendPanel = e.Find<StackPanel>("switch2ViiperLegendPanel");
            heavyRumbleTestBtn = e.Find<Button>("heavyRumbleTestBtn");
            lightRumbleTestBtn = e.Find<Button>("lightRumbleTestBtn");
            useControllerUD = e.Find<Xceed.Wpf.Toolkit.IntegerUpDown>("useControllerUD");
            InitialiseAdvancedPage();
        }
    }
}
