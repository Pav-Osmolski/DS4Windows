/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Threading;
using Ookii.Dialogs.Wpf;
using DS4WinWPF.DS4Forms.ViewModels;
using Microsoft.Win32;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for AutoProfiles.xaml
    /// </summary>
    public partial class AutoProfiles : UserControl
    {
        protected String m_Profile = DS4Windows.Global.appdatapath + "\\Auto Profiles.xml";
        public const string steamCommx86Loc = @"C:\Program Files (x86)\Steam\steamapps\common";
        public const string steamCommLoc = @"C:\Program Files\Steam\steamapps\common";
        private string steamgamesdir;
        private AutoProfilesViewModel autoProfVM;
        private AutoProfileHolder autoProfileHolder;
        private ProfileList profileList;
        private ListCollectionView autoProfilesCollectionView;
        private bool autoDebug;
        private CancellationTokenSource scanCancellation;

        public AutoProfileHolder AutoProfileHolder { get => autoProfileHolder;
            set => autoProfileHolder = value; }
        public AutoProfilesViewModel AutoProfVM { get => autoProfVM; }
        public bool AutoDebug { get => autoDebug; }
        public event EventHandler AutoDebugChanged;

        public AutoProfiles() : this(null) { }

        internal AutoProfiles(AutoProfileHolder holder)
        {
            InitializeComponent();

            if (holder == null && !File.Exists(DS4Windows.Global.appdatapath + @"\Auto Profiles.xml"))
                DS4Windows.Global.CreateAutoProfiles(m_Profile);

            //LoadP();

            if (DS4Windows.Global.UseCustomSteamFolder &&
                Directory.Exists(DS4Windows.Global.CustomSteamFolder))
                steamgamesdir = DS4Windows.Global.CustomSteamFolder;
            else if (Directory.Exists(steamCommx86Loc))
                steamgamesdir = steamCommx86Loc;
            else if (Directory.Exists(steamCommLoc))
                steamgamesdir = steamCommLoc;
            else
                addProgramsBtn.ContextMenu.Items.Remove(steamMenuItem);

            autoProfileHolder = holder ?? new AutoProfileHolder();

            int currentRowCount = autoProfilesGrid.RowDefinitions.Count;
            if (currentRowCount > DS4Windows.ControlService.CURRENT_DS4_CONTROLLER_LIMIT)
            {
                for (int i = currentRowCount-1; i >= DS4Windows.ControlService.CURRENT_DS4_CONTROLLER_LIMIT; i--)
                {
                    autoProfilesGrid.RowDefinitions.RemoveAt(i);
                }
            }
        }

        public void SetupDataContext(ProfileList profileList)
        {
            autoProfVM = new AutoProfilesViewModel(autoProfileHolder, profileList);
            programListLV.DataContext = autoProfVM;
            autoProfilesCollectionView = new ListCollectionView(autoProfVM.ProgramColl)
            {
                Filter = AutoProfileMatchesSearch,
            };
            programListLV.ItemsSource = autoProfilesCollectionView;
            
            revertDefaultProfileOnUnknownCk.DataContext = autoProfVM;

            autoProfVM.CurrentItemChange += AutoProfVM_CurrentItemChange;
            autoProfVM.ProgramColl.CollectionChanged += (_, _) => UpdateRuleActions();

            //autoProfilesGrid.DataContext = autoProfVM;
            outerEditControlsPanel.DataContext = autoProfVM;
            editControlsPanel.DataContext = autoProfVM.SelectedItem;
            this.profileList = profileList;

            // Display the actual persisted priority; sorting would hide moves.
            autoProfilesCollectionView.SortDescriptions.Clear();
            autoProfilesCollectionView.Refresh();
            UpdateRuleActions();

            sidebarScrollViewer.ScrollToTop();
        }

        private void AutoProfVM_CurrentItemChange(AutoProfilesViewModel sender, ProgramItem item)
        {
            if (item != null)
            {
                if (item.MatchedAutoProfile != null)
                {
                    ProfileEntity tempProf = null;
                    string profileName = item.MatchedAutoProfile.ProfileNames[0];
                    if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                    {
                        tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                        if (tempProf != null)
                        {
                            item.SelectedIndexCon1 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                        }
                    }
                    else
                    {
                        item.SelectedIndexCon1 = 0;
                    }

                    profileName = item.MatchedAutoProfile.ProfileNames[1];
                    if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                    {
                        tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                        if (tempProf != null)
                        {
                            item.SelectedIndexCon2 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                        }
                    }
                    else
                    {
                        item.SelectedIndexCon2 = 0;
                    }

                    profileName = item.MatchedAutoProfile.ProfileNames[2];
                    if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                    {
                        tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                        if (tempProf != null)
                        {
                            item.SelectedIndexCon3 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                        }
                    }
                    else
                    {
                        item.SelectedIndexCon3 = 0;
                    }

                    profileName = item.MatchedAutoProfile.ProfileNames[3];
                    if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                    {
                        tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                        if (tempProf != null)
                        {
                            item.SelectedIndexCon4 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                        }
                    }
                    else
                    {
                        item.SelectedIndexCon4 = 0;
                    }

                    if (autoProfVM.UsingExpandedControllers)
                    {
                        profileName = item.MatchedAutoProfile.ProfileNames[4];
                        if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                        {
                            tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                            if (tempProf != null)
                            {
                                item.SelectedIndexCon5 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                            }
                        }
                        else
                        {
                            item.SelectedIndexCon5 = 0;
                        }

                        profileName = item.MatchedAutoProfile.ProfileNames[5];
                        if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                        {
                            tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                            if (tempProf != null)
                            {
                                item.SelectedIndexCon6 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                            }
                        }
                        else
                        {
                            item.SelectedIndexCon6 = 0;
                        }

                        profileName = item.MatchedAutoProfile.ProfileNames[6];
                        if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                        {
                            tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                            if (tempProf != null)
                            {
                                item.SelectedIndexCon7 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                            }
                        }
                        else
                        {
                            item.SelectedIndexCon7 = 0;
                        }

                        profileName = item.MatchedAutoProfile.ProfileNames[7];
                        if (!string.IsNullOrEmpty(profileName) && profileName != "(none)")
                        {
                            tempProf = profileList.ProfileListCol.SingleOrDefault(x => x.Name == profileName);
                            if (tempProf != null)
                            {
                                item.SelectedIndexCon8 = profileList.ProfileListCol.IndexOf(tempProf) + 1;
                            }
                        }
                        else
                        {
                            item.SelectedIndexCon8 = 0;
                        }
                    }
                }

                editControlsPanel.DataContext = item;
                editControlsPanel.IsEnabled = scanCancellation == null;
            }
            else
            {
                editControlsPanel.DataContext = null;
                editControlsPanel.IsEnabled = false;

                cont1AutoProf.SelectedIndex = 0;
                cont2AutoProf.SelectedIndex = 0;
                cont3AutoProf.SelectedIndex = 0;
                cont4AutoProf.SelectedIndex = 0;
                cont5AutoProf.SelectedIndex = 0;
                cont6AutoProf.SelectedIndex = 0;
                cont7AutoProf.SelectedIndex = 0;
                cont8AutoProf.SelectedIndex = 0;
            }
            UpdateRuleActions();
        }

        private async Task RunProgramScanAsync(string label,
            Func<CancellationToken, IProgress<ProgramScanProgress>, Task<ProgramScanResult>> scan)
        {
            if (scanCancellation != null) return;
            using var cancellation = new CancellationTokenSource();
            scanCancellation = cancellation;
            scanStatusPanel.Visibility = Visibility.Visible;
            scanProgress.Visibility = cancelScanBtn.Visibility = Visibility.Visible;
            scanStatusText.Text = label;
            scanLocationText.Text = global::DS4Windows.UiText.Get("Looking for programs…");
            addProgramsBtn.IsEnabled = hideUncheckedBtn.IsEnabled = editControlsPanel.IsEnabled = false;
            UpdateRuleActions();
            var progress = new Progress<ProgramScanProgress>(state =>
            {
                if (scanCancellation != cancellation) return;
                scanStatusText.Text = global::DS4Windows.UiText.Format("{0} · {1:N0} checked · {2:N0} found", label, state.Checked, state.Found);
                scanLocationText.Text = state.Location;
            });
            try
            {
                ProgramScanResult result = await scan(cancellation.Token, progress);
                scanStatusText.Text = global::DS4Windows.UiText.Format("Added {0:N0} program(s). {1:N0} already listed. {2:N0} unavailable or unsupported item(s) skipped.", result.Added, result.Programs.Count - result.Added, result.Skipped);
                scanLocationText.Text = result.Programs.Count == 0
                    ? global::DS4Windows.UiText.Get("No executable programs found. Try Browse for Other Programs or Add Window Title Rule.")
                    : result.Added == 0 ? global::DS4Windows.UiText.Get("All discovered programs are already listed. Use search to find and configure them.")
                    : global::DS4Windows.UiText.Get("Choose a program, configure its profiles, then Save rule. Only enabled, saved rules are applied.");
            }
            catch (OperationCanceledException)
            {
                scanStatusText.Text = global::DS4Windows.UiText.Get("Scan cancelled. No programs were added.");
                scanLocationText.Text = global::DS4Windows.UiText.Get("Your existing rules have been kept.");
            }
            catch (Exception error)
            {
                scanStatusText.Text = global::DS4Windows.UiText.Get("Could not finish scanning programs.");
                scanLocationText.Text = error.Message;
            }
            finally
            {
                scanCancellation = null;
                scanProgress.Visibility = cancelScanBtn.Visibility = Visibility.Collapsed;
                addProgramsBtn.IsEnabled = hideUncheckedBtn.IsEnabled = true;
                editControlsPanel.IsEnabled = autoProfVM.SelectedItem != null;
                UpdateRuleActions();
            }
        }

        private async void SteamMenuItem_Click(object sender, RoutedEventArgs e) =>
            await RunProgramScanAsync(global::DS4Windows.UiText.Get("Scanning Steam games"), (token, progress) => autoProfVM.AddProgramsFromDir(steamgamesdir, token, progress));

        private async void BrowseProgsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog();
            if (dialog.ShowDialog() == true)
                await RunProgramScanAsync("Scanning directory", (token, progress) => autoProfVM.AddProgramsFromDir(dialog.SelectedPath, token, progress));
        }

        private async void StartMenuItem_Click(object sender, RoutedEventArgs e) =>
            await RunProgramScanAsync(global::DS4Windows.UiText.Get("Scanning Start Menu"), (token, progress) => autoProfVM.AddProgramsFromStartMenu(token, progress));

        private void CancelScanBtn_Click(object sender, RoutedEventArgs e) => scanCancellation?.Cancel();

        private void AddProgramsBtn_Click(object sender, RoutedEventArgs e)
        {
            addProgramsBtn.ContextMenu.PlacementTarget = addProgramsBtn;
            addProgramsBtn.ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void HideUncheckedBtn_Click(object sender, RoutedEventArgs e)
        {
            autoProfVM.RemoveUnchecked();
            UpdateRuleActions();
        }

        private void ShowAutoDebugCk_Click(object sender, RoutedEventArgs e)
        {
            bool state = showAutoDebugCk.IsChecked == true;
            autoDebug = state;
            AutoDebugChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool AutoProfileMatchesSearch(object value)
        {
            if (value is not ProgramItem item)
            {
                return false;
            }

            string query = autoProfilesSearchTextBox?.Text?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            bool Contains(string candidate) =>
                candidate?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;

            return Contains(item.Filename) || Contains(item.Path) || Contains(item.Title) ||
                (item.MatchedAutoProfile?.ProfileNames?.Any(Contains) ?? false);
        }

        private void AutoProfilesSearchTextBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            clearAutoProfilesSearchBtn.Visibility =
                string.IsNullOrWhiteSpace(autoProfilesSearchTextBox.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            autoProfilesCollectionView?.Refresh();
            UpdateRuleActions();
        }

        private void ClearAutoProfilesSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            autoProfilesSearchTextBox.Clear();
            autoProfilesSearchTextBox.Focus();
        }

        private void RemoveAutoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (autoProfVM.SelectedItem != null)
            {
                ProgramItem item = autoProfVM.SelectedItem;
                bool saved = item.MatchedAutoProfile != null;
                if (saved && MessageBox.Show("Delete this saved rule? Your controller profiles will be kept.",
                    "Delete Auto Profile rule", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                bool removeHidHide = item.MatchedAutoProfile?.Turnoff == true;
                autoProfVM.RemoveAutoProfileEntry(item);
                if (removeHidHide) autoProfVM.AddExeToHIDHideWhenSaving(item, HasEnabledTurnoffRule(item.Path));
                SaveRules();
                UpdateRuleActions();
            }
        }

        private bool HasEnabledTurnoffRule(string path) => autoProfileHolder.AutoProfileColl.Any(rule =>
            rule.Enabled && rule.Turnoff && string.Equals(rule.Path, path, StringComparison.OrdinalIgnoreCase));

        private void SaveRules()
        {
            if (!autoProfileHolder.Save(m_Profile))
            {
                scanStatusPanel.Visibility = Visibility.Visible;
                scanStatusText.Text = global::DS4Windows.UiText.Get("Could not save Auto Profiles.");
                scanLocationText.Text = global::DS4Windows.UiText.Get("Check that the settings folder is writable, then try Save rule again.");
            }
        }

        private void SaveAutoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (autoProfVM.SelectedItem != null)
            {
                if (string.IsNullOrWhiteSpace(autoProfVM.SelectedItem.Path) && string.IsNullOrWhiteSpace(autoProfVM.SelectedItem.Title))
                {
                    scanStatusPanel.Visibility = Visibility.Visible;
                    scanStatusText.Text = global::DS4Windows.UiText.Get("Enter a program path or window title before saving this rule.");
                    scanLocationText.Text = global::DS4Windows.UiText.Get("Blank rules cannot match a program.");
                    return;
                }
                if (autoProfVM.SelectedItem.MatchedAutoProfile == null)
                {
                    autoProfVM.CreateAutoProfileEntry(autoProfVM.SelectedItem);
                }
                else
                {
                    autoProfVM.PersistAutoProfileEntry(autoProfVM.SelectedItem);
                }

                autoProfVM.SelectedItem.RefreshState();
                autoProfVM.AddExeToHIDHideWhenSaving(autoProfVM.SelectedItem, HasEnabledTurnoffRule(autoProfVM.SelectedItem.Path));
                SaveRules();
                UpdateRuleActions();
            }
        }

        private void DuplicateAutoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (autoProfVM.SelectedItem != null)
            {
                ProgramItem duplicateItem = autoProfVM.DuplicateAutoProfileEntry(autoProfVM.SelectedItem);

                if (duplicateItem != null)
                {
                    programListLV.SelectedItem = duplicateItem;
                    programListLV.ScrollIntoView(duplicateItem);
                }
            }
        }

        private void BrowseAddProgMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = false, AddExtension = true, DefaultExt = ".exe",
                Filter = global::DS4Windows.UiText.FileFilter("Program (*.exe)|*.exe"), Title = global::DS4Windows.UiText.Get("Select Program"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dialog.ShowDialog() == true)
            {
                ProgramItem item = autoProfVM.AddProgramExeLocation(dialog.FileName);
                programListLV.SelectedItem = item;
                programListLV.ScrollIntoView(item);
            }
        }

        private void WindowTitleRuleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            autoProfVM.AddWindowTitleRule();
            programListLV.SelectedItem = autoProfVM.SelectedItem;
            programListLV.ScrollIntoView(autoProfVM.SelectedItem);
        }

        private void UpdateRuleActions()
        {
            if (autoProfVM == null || moveRuleUpBtn == null) return;
            int index = autoProfVM.ProgramColl.IndexOf(autoProfVM.SelectedItem);
            bool canMove = scanCancellation == null && string.IsNullOrWhiteSpace(autoProfilesSearchTextBox.Text);
            moveRuleUpBtn.IsEnabled = MoveUp.IsEnabled = canMove && index > 0;
            moveRuleDownBtn.IsEnabled = MoveDown.IsEnabled = canMove && index >= 0 && index < autoProfVM.ProgramColl.Count - 1;
        }

        private void ProgramListLV_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Right) return;
            if (ItemsControl.ContainerFromElement(programListLV, e.OriginalSource as DependencyObject) is ListViewItem item)
                programListLV.SelectedItem = item.Content;
            UpdateRuleActions();
        }

        private void MoveUpDownAutoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (scanCancellation != null || !string.IsNullOrWhiteSpace(autoProfilesSearchTextBox.Text)) return;
            int direction = sender == MoveUp || sender == moveRuleUpBtn ? -1 : 1;
            var item = autoProfVM.SelectedItem;
            if (item != null && autoProfVM.MoveItemUpDown(item, direction))
            {
                SaveRules();
                programListLV.SelectedItem = item;
                programListLV.ScrollIntoView(item);
            }
            UpdateRuleActions();
        }
    }
}
