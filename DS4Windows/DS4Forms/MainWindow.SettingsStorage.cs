using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

public partial class MainWindow
{
    private ProfileBrowserViews profileBrowserViews;
    private LatestPreferenceWriter<ProfileViewChoice> profileViewWriter;
    private void ProfileViewChoices_Loaded(object sender, RoutedEventArgs e)
    {
        if (profileBrowserViews != null) return;
        profileBrowserViews = new ProfileBrowserViews(profilesListBox, profileDetailsHeader);
        profileIconBtn.Content = UiText.Get("Profile icon…");
        profileNameHeader.Text = UiText.Get("Name");
        profileOutputHeader.Text = UiText.Get("Emulated controller");
        profileModifiedHeader.Text = UiText.Get("Last modified");
        var choice = ProfileViewChoice.Tiles;
        try { choice = SettingsStorage.Current.ProfileView; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { NLog.LogManager.GetLogger("Profiles").Warn(error, "Could not read the profile view preference."); }
        profileViewWriter = new(value => SettingsStorage.Current.SetProfileView(value),
            error => NLog.LogManager.GetLogger("Profiles").Warn(error, "Could not remember the profile view."));
        profileViewWriter.Initialize(choice);
        ProfileBrowserViews.CreatePicker(profileViewChoices, choice,
            value => { profileBrowserViews.Apply(value); profileViewWriter.Queue(value); });
        profileBrowserViews.Apply(choice);
    }

    private void ProfileIconBtn_Click(object sender, RoutedEventArgs e)
    {
        if (profilesListBox.SelectedItem is not ProfileEntity profile || !RequireWritableProfiles()) return;
        foreach (var controller in conLvViewModel.ControllerCol)
            if (controller.SelectedProfile == profile.Name) FlushOverviewQuickSettings(controller.DevIndex, false);
        if (HasPendingOverviewSave(profile.Name)) return;
        if (new ProfileIconWindow(profile) { Owner = this }.ShowDialog() == true)
            profilesCollectionView?.Refresh();
    }

    private void ProfileSortCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (profileSortCombo.Items.Count > 0) return;
        profileSortCombo.Items.Add(UiText.Get("Name: A–Z"));
        profileSortCombo.Items.Add(UiText.Get("Last modified"));
        profileSortCombo.SelectedIndex = 0;
    }

    private void ProfileSortCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (profilesListBox?.ItemsSource == null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(profilesListBox.ItemsSource);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            if (profileSortCombo.SelectedIndex == 1)
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ProfileEntity.ModifiedUtc), System.ComponentModel.ListSortDirection.Descending));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ProfileEntity.Name), System.ComponentModel.ListSortDirection.Ascending));
        }
    }

    private bool BeforeSettingsLocationChange()
    {
        if (mainWinVM?.ProfileEditorMode == true || overviewFailedWrites.Count > 0 || overviewDirtyControllerIndices.Count > 0)
        {
            MessageBox.Show(this, UiText.Get("Save or discard your profile changes and close the profile editor before changing or backing up settings."),
                "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!Global.Save())
        {
            // A read-only source must still be migratable. Copy the last saved
            // configuration; do not imply that unsaved session changes survived.
            return MessageBox.Show(this, UiText.Get("Current application settings could not be saved. Continue using the last saved files? Unsaved changes will not be included."),
                "DS4Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
        return true;
    }

    private bool RequireWritableProfiles()
    {
        if (SettingsStorage.CanWrite(System.IO.Path.Combine(Global.appdatapath, "Profiles"), out string reason)) return true;
        MessageBox.Show(this, UiText.Get("This profiles folder is read-only. Use Settings & profiles to migrate to AppData before saving changes.") + "\n\n" + reason,
            "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
