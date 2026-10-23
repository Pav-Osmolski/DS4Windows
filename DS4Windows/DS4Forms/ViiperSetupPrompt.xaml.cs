using System.Windows;
using System.Windows.Input;

namespace DS4WinWPF.DS4Forms
{
    public enum ViiperSetupPromptDecision
    {
        NotNow,
        InstallStandard,
        InstallPortable,
    }

    public partial class ViiperSetupPrompt : Window
    {
        private readonly bool mandatoryRepairRequired;

        public ViiperSetupPromptDecision Decision { get; private set; } =
            ViiperSetupPromptDecision.NotNow;

        public bool SuppressFuturePrompts =>
            suppressPromptCheck.IsChecked == true;

        public bool ExitApplicationRequested => false;

        public ViiperSetupPrompt(string currentStatus,
            string existingViiperPath, bool citrixUsbMonitorConflict = false,
            bool verifiedUpdateRequired = false,
            bool usbipReplacementRequired = false,
            bool mandatoryRepairRequired = false)
        {
            this.mandatoryRepairRequired = mandatoryRepairRequired ||
                verifiedUpdateRequired;
            InitializeComponent();
            statusText.Text = currentStatus;

            if (citrixUsbMonitorConflict)
            {
                headingText.Text = global::DS4Windows.UiText.Get("VIIPER paused for system safety");
                summaryText.Text =
                    global::DS4Windows.UiText.Get("A conflicting Citrix USB monitor is active.");
                requirementsText.Text =
                    global::DS4Windows.UiText.Get("• Disable Citrix generic USB redirection only\n") +
                    global::DS4Windows.UiText.Get("• Restart Windows before VIIPER starts again");
                installButton.Content = "Disable conflicting USB monitor";
                installPortableButton.Visibility = Visibility.Collapsed;
                portableWarningPanel.Visibility = Visibility.Collapsed;
                suppressPromptCheck.Visibility = Visibility.Collapsed;
                notNowButton.Content = "Continue without virtual output";
                closeButton.ToolTip = global::DS4Windows.UiText.Get("Continue without virtual output");
                return;
            }

            if (!string.IsNullOrWhiteSpace(existingViiperPath))
            {
                existingViiperPathText.Text = existingViiperPath;
                existingViiperPanel.Visibility = Visibility.Visible;
            }

            if (verifiedUpdateRequired)
            {
                headingText.Text = usbipReplacementRequired
                    ? global::DS4Windows.UiText.Get("USB-IP version must be replaced")
                    : global::DS4Windows.UiText.Get("VIIPER verification failed");
                summaryText.Text = usbipReplacementRequired
                    ? currentStatus
                    : global::DS4Windows.UiText.Get("The installed VIIPER does not match this DS4Windows package.");
                requirementsHeadingText.Text = global::DS4Windows.UiText.Get("Verified update required");
                requirementsText.Text = usbipReplacementRequired
                    ? global::DS4Windows.UiText.Get("• Install and verify bundled VIIPER 0.1.10\n") +
                      global::DS4Windows.UiText.Get("• Safely remove the unsupported USB-IP package\n") +
                      global::DS4Windows.UiText.Get("• Restart, then finish installing USB-IP 0.9.8.1")
                    : global::DS4Windows.UiText.Get("• Install the exact bundled VIIPER build\n") +
                      global::DS4Windows.UiText.Get("• Keep your existing installation location\n") +
                      global::DS4Windows.UiText.Get("• The unverified backend will not be started");
                installButton.Content = usbipReplacementRequired
                    ? "Repair VIIPER + USB-IP"
                    : "Install / Repair";
                installPortableButton.Content =
                    "Keep DS4Windows portable";
                existingViiperPanel.Visibility = Visibility.Collapsed;
                suppressPromptCheck.Visibility = Visibility.Collapsed;
                notNowButton.Content = "Continue without virtual output";
                closeButton.ToolTip = global::DS4Windows.UiText.Get("Continue without virtual output");
            }
            else if (this.mandatoryRepairRequired)
            {
                headingText.Text = global::DS4Windows.UiText.Get("VIIPER setup required");
                summaryText.Text = currentStatus;
                requirementsHeadingText.Text = global::DS4Windows.UiText.Get("Required before DS4Windows can run");
                requirementsText.Text =
                    global::DS4Windows.UiText.Get("• Install the bundled VIIPER 0.1.10 build\n") +
                    global::DS4Windows.UiText.Get("• Install and verify USB-IP 0.9.8.1\n") +
                    global::DS4Windows.UiText.Get("• Start DS4Windows only after the runtime probe passes");
                installButton.Content = "Install / Repair";
                installPortableButton.Content = "Keep DS4Windows portable";
                suppressPromptCheck.Visibility = Visibility.Collapsed;
                notNowButton.Content = "Continue without virtual output";
                closeButton.ToolTip = global::DS4Windows.UiText.Get("Continue without virtual output");
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            Decision = ViiperSetupPromptDecision.InstallStandard;
            DialogResult = true;
        }

        private void InstallPortableButton_Click(object sender,
            RoutedEventArgs e)
        {
            Decision = ViiperSetupPromptDecision.InstallPortable;
            DialogResult = true;
        }

        private void NotNowButton_Click(object sender, RoutedEventArgs e)
        {
            Decision = ViiperSetupPromptDecision.NotNow;
            DialogResult = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Decision = ViiperSetupPromptDecision.NotNow;
            DialogResult = false;
        }
    }
}
