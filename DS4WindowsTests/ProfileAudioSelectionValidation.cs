using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Data;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

internal static class ProfileAudioSelectionValidation
{
    internal static void Validate()
    {
        string previousSource = Global.DualSenseAudioCaptureEndpointId[0];
        string previousSpeaker = Global.DualSenseAudioSpeakerEndpointId[0];
        try
        {
            foreach (var sourceId in new[] { "", DualSenseAudioPassthrough.AutoDetectGameAudioEndpointId,
                DualSenseAudioPassthrough.DefaultSystemAudioEndpointId, "saved-source-unavailable", null })
            foreach (var speakerId in new[] { "", "saved-speaker-unavailable", null })
            {
                Global.DualSenseAudioCaptureEndpointId[0] = sourceId;
                Global.DualSenseAudioSpeakerEndpointId[0] = speakerId;
                var source = Combo(nameof(ProfileSettingsViewModel.AudioCaptureEndpointChoices), nameof(ProfileSettingsViewModel.DualSenseAudioCaptureEndpointId));
                var speaker = Combo(nameof(ProfileSettingsViewModel.AudioSpeakerEndpointChoices), nameof(ProfileSettingsViewModel.DualSenseAudioSpeakerEndpointId));
                string expectedSource = sourceId == DualSenseAudioPassthrough.AutoDetectGameAudioEndpointId ? "" : sourceId ?? "";
                string expectedSpeaker = speakerId ?? "";
                for (int visit = 0; visit < 3; visit++)
                {
                    var vm = (ProfileSettingsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ProfileSettingsViewModel));
                    source.DataContext = speaker.DataContext = vm;
                    Check(source, expectedSource); Check(speaker, expectedSpeaker);
                    for (int refresh = 0; refresh < 3; refresh++)
                    {
                        vm.NotifyAudioEndpointChoicesChanged();
                        Check(source, expectedSource); Check(speaker, expectedSpeaker);
                    }
                    source.DataContext = speaker.DataContext = null;
                }
            }
        }
        finally
        {
            Global.DualSenseAudioCaptureEndpointId[0] = previousSource;
            Global.DualSenseAudioSpeakerEndpointId[0] = previousSpeaker;
        }
    }

    private static ComboBox Combo(string choices, string selection)
    {
        var combo = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "EndpointId" };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(choices));
        combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
            new Binding(selection) { Mode = BindingMode.TwoWay });
        return combo;
    }

    private static void Check(ComboBox combo, string expected)
    {
        combo.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.AreEqual(expected, combo.SelectedValue, "Saved audio selection must survive opening the editor and refreshing its endpoint list.");
        Assert.IsNotNull(combo.SelectedItem, "The dropdown must display the saved/automatic choice.");
    }
}
