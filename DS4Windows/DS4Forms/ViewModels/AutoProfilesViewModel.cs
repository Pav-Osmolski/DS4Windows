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

using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DS4WinWPF.DS4Forms.ViewModels
{
    public class AutoProfilesViewModel
    {
        private object _colLockobj = new object();
        private ObservableCollection<ProgramItem> programColl;
        private AutoProfileHolder autoProfileHolder;
        private ProfileList profileList;
        private int selectedIndex = -1;
        private ProgramItem selectedItem;
        private HashSet<string> existingapps;

        public ObservableCollection<ProgramItem> ProgramColl { get => programColl; }
        
        public AutoProfileHolder AutoProfileHolder { get => autoProfileHolder; }

        public int SelectedIndex { get => selectedIndex; set => selectedIndex = value; }
        public ProgramItem SelectedItem
        {
            get => selectedItem;
            set
            {
                selectedItem = value;
                CurrentItemChange?.Invoke(this, value);
            }
        }

        public ProfileList ProfileList { get => profileList; }

        public delegate void CurrentItemChangeHandler(AutoProfilesViewModel sender, ProgramItem item);
        public event CurrentItemChangeHandler CurrentItemChange;

        public delegate void AutoProfileStateHandler(AutoProfilesViewModel sender, bool state);
        public event AutoProfileStateHandler AutoProfileSystemChange;

        public bool RevertDefaultProfileOnUnknown
        {
            get => Global.AutoProfileRevertDefaultProfile;
            set => Global.AutoProfileRevertDefaultProfile = value;
        }

        public bool UsingExpandedControllers
        {
            get => ControlService.USING_MAX_CONTROLLERS;
        }

        private List<EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>> displayProfileSwitchList =
            new List<EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>>()
            {
                new EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>(Translations.Strings.None, AutoProfileDisplayProfileSwitchChoices.None),
                new EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>(Translations.Strings.Log, AutoProfileDisplayProfileSwitchChoices.Log),
                new EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>(Translations.Strings.Notification, AutoProfileDisplayProfileSwitchChoices.Notification),
                new EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>(Translations.Strings.AutoProfiles_LogAndNotification, AutoProfileDisplayProfileSwitchChoices.LogAndNotification),
            };

        public List<EnumChoiceSelection<AutoProfileDisplayProfileSwitchChoices>> DisplayProfileSwitchList => displayProfileSwitchList;

        private List<EnumChoiceSelection<AutoProfileDeviceOption>> deviceOptionList =
            new List<EnumChoiceSelection<AutoProfileDeviceOption>>()
            {
                new EnumChoiceSelection<AutoProfileDeviceOption>("Any", AutoProfileDeviceOption.Any),
                new EnumChoiceSelection<AutoProfileDeviceOption>("DualSense", AutoProfileDeviceOption.DualSense),
                new EnumChoiceSelection<AutoProfileDeviceOption>("DS4", AutoProfileDeviceOption.DS4),
                new EnumChoiceSelection<AutoProfileDeviceOption>("DS3", AutoProfileDeviceOption.DS3),
                new EnumChoiceSelection<AutoProfileDeviceOption>("Switch Pro", AutoProfileDeviceOption.SwitchPro),
                new EnumChoiceSelection<AutoProfileDeviceOption>("JoyCons", AutoProfileDeviceOption.JoyCons),
            };

        public List<EnumChoiceSelection<AutoProfileDeviceOption>> DeviceOptionList => deviceOptionList;

        public AutoProfileDisplayProfileSwitchChoices ProfileSwitchChoice
        {
            get => Global.autoProfileSwitchNotifyChoice;
            set => Global.autoProfileSwitchNotifyChoice = value;
        }

        public AutoProfilesViewModel(AutoProfileHolder autoProfileHolder, ProfileList profileList)
        {
            programColl = new ObservableCollection<ProgramItem>();
            existingapps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.autoProfileHolder = autoProfileHolder;
            this.profileList = profileList;
            PopulateCurrentEntries();

            BindingOperations.EnableCollectionSynchronization(programColl, _colLockobj);
        }

        private void PopulateCurrentEntries()
        {
            foreach(AutoProfileEntity entry in autoProfileHolder.AutoProfileColl)
            {
                ProgramItem item = new ProgramItem(entry.Path, entry);

                programColl.Add(item);
                existingapps.Add(entry.Path);
            }
        }

        public void RemoveUnchecked()
        {
            AutoProfileSystemChange?.Invoke(this, false);
            foreach (var item in programColl.Where(row => row.MatchedAutoProfile == null).ToArray()) RemoveAutoProfileEntry(item);
            AutoProfileSystemChange?.Invoke(this, true);
        }

        internal Task<ProgramScanResult> AddProgramsFromStartMenu(CancellationToken token, IProgress<ProgramScanProgress> progress) =>
            AddScannedProgramsAsync(ProgramScanner.StartMenuAsync(token, progress), token);

        internal Task<ProgramScanResult> AddProgramsFromDir(string location, CancellationToken token, IProgress<ProgramScanProgress> progress) =>
            AddScannedProgramsAsync(Task.Run(() => ProgramScanner.Directory(location, token, progress), token), token);

        private async Task<ProgramScanResult> AddScannedProgramsAsync(Task<ProgramScanResult> scan, CancellationToken token)
        {
            ProgramScanResult result = await scan.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            int added = 0;
            // Resume on the UI dispatcher. Background discovery never touches either collection.
            existingapps.Clear();
            foreach (var item in programColl)
                if (!string.IsNullOrEmpty(item.Path)) existingapps.Add(item.Path);
            foreach (string path in result.Programs)
                if (existingapps.Add(path)) { programColl.Add(new ProgramItem(path)); added++; }
            return result with { Added = added };
        }

        internal ProgramItem AddProgramExeLocation(string path)
        {
            ProgramItem item = new(path);
            programColl.Add(item);
            existingapps.Add(path);
            SelectedItem = item;
            return item;
        }

        public void AddWindowTitleRule()
        {
            ProgramItem item = new ProgramItem(string.Empty);
            programColl.Add(item);
            SelectedItem = item;
            SelectedIndex = programColl.IndexOf(item);
        }

        public void CreateAutoProfileEntry(ProgramItem item)
        {
            if (item.MatchedAutoProfile == null)
            {
                AutoProfileEntity tempEntry = new AutoProfileEntity(item.Path, item.Title);
                tempEntry.Turnoff = item.Turnoff;
                tempEntry.Enabled = item.Enabled;
                tempEntry.DeviceOption = item.DeviceOption;
                tempEntry.ApplyToAllControllers = item.ApplyToAllControllers;
                int tempindex = item.SelectedIndexCon1;
                tempEntry.ProfileNames[0] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                tempindex = item.SelectedIndexCon2;
                tempEntry.ProfileNames[1] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                tempindex = item.SelectedIndexCon3;
                tempEntry.ProfileNames[2] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                tempindex = item.SelectedIndexCon4;
                tempEntry.ProfileNames[3] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                if (UsingExpandedControllers)
                {
                    tempindex = item.SelectedIndexCon5;
                    tempEntry.ProfileNames[4] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;

                    tempindex = item.SelectedIndexCon6;
                    tempEntry.ProfileNames[5] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;

                    tempindex = item.SelectedIndexCon7;
                    tempEntry.ProfileNames[6] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;

                    tempindex = item.SelectedIndexCon8;
                    tempEntry.ProfileNames[7] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;
                }

                item.MatchedAutoProfile = tempEntry;
                int index = programColl.Take(programColl.IndexOf(item)).Count(row => row.MatchedAutoProfile != null);
                autoProfileHolder.AutoProfileColl.Insert(index, item.MatchedAutoProfile);
            }
        }

        public void PersistAutoProfileEntry(ProgramItem item)
        {
            if (item.MatchedAutoProfile != null)
            {
                AutoProfileEntity tempEntry = item.MatchedAutoProfile;
                tempEntry.Path = item.Path;
                tempEntry.Title = item.Title;
                tempEntry.Turnoff = item.Turnoff;
                tempEntry.Enabled = item.Enabled;
                tempEntry.DeviceOption = item.DeviceOption;
                tempEntry.ApplyToAllControllers = item.ApplyToAllControllers;
                int tempindex = item.SelectedIndexCon1;
                tempEntry.ProfileNames[0] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                tempindex = item.SelectedIndexCon2;
                tempEntry.ProfileNames[1] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                tempindex = item.SelectedIndexCon3;
                tempEntry.ProfileNames[2] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                tempindex = item.SelectedIndexCon4;
                tempEntry.ProfileNames[3] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                    AutoProfileEntity.NONE_STRING;

                if (UsingExpandedControllers)
                {
                    tempindex = item.SelectedIndexCon5;
                    tempEntry.ProfileNames[4] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;

                    tempindex = item.SelectedIndexCon6;
                    tempEntry.ProfileNames[5] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;

                    tempindex = item.SelectedIndexCon7;
                    tempEntry.ProfileNames[6] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;

                    tempindex = item.SelectedIndexCon8;
                    tempEntry.ProfileNames[7] = tempindex > 0 ? profileList.ProfileListCol[tempindex - 1].Name :
                        AutoProfileEntity.NONE_STRING;
                }
            }
            item.RefreshState();
        }

        public ProgramItem DuplicateAutoProfileEntry(ProgramItem item)
        {
            if (item == null) return null;
            // Duplicating is an edit operation, not an implicit Save of the source rule.
            var duplicate = new ProgramItem(item.Path)
            {
                Title = item.Title, Enabled = item.Enabled, Turnoff = item.Turnoff,
                DeviceOption = item.DeviceOption, ApplyToAllControllers = item.ApplyToAllControllers,
                SelectedIndexCon1 = item.SelectedIndexCon1, SelectedIndexCon2 = item.SelectedIndexCon2,
                SelectedIndexCon3 = item.SelectedIndexCon3, SelectedIndexCon4 = item.SelectedIndexCon4,
                SelectedIndexCon5 = item.SelectedIndexCon5, SelectedIndexCon6 = item.SelectedIndexCon6,
                SelectedIndexCon7 = item.SelectedIndexCon7, SelectedIndexCon8 = item.SelectedIndexCon8
            };
            programColl.Insert(programColl.IndexOf(item) + 1, duplicate);
            if (!string.IsNullOrEmpty(item.Path)) existingapps.Add(item.Path);
            SelectedItem = duplicate;
            SelectedIndex = programColl.IndexOf(duplicate);
            return duplicate;
        }

        public void RemoveAutoProfileEntry(ProgramItem item)
        {
            if (item == null) return;
            if (item.MatchedAutoProfile != null) autoProfileHolder.AutoProfileColl.Remove(item.MatchedAutoProfile);
            programColl.Remove(item);
            existingapps.Clear();
            foreach (var remaining in programColl) if (!string.IsNullOrEmpty(remaining.Path)) existingapps.Add(remaining.Path);
            if (SelectedItem == item) SelectedItem = null;
        }

        public string ResolveShortcutAndArgument(string filePath)
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            string result;

            try
            {
                var shortcut = shell.CreateShortcut(filePath);
                result = shortcut.TargetPath + " " + shortcut.Arguments;
                Marshal.FinalReleaseComObject(shortcut);
            }
            catch (COMException)
            {
                // A COMException is thrown if the file is not a valid shortcut (.lnk) file 
                result = null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }

            return result;
        }

        public string ResolveMsiShortcut(string file)
        {
            StringBuilder product = new StringBuilder(NativeMethods2.MaxGuidLength + 1);
            StringBuilder feature = new StringBuilder(NativeMethods2.MaxFeatureLength + 1);
            StringBuilder component = new StringBuilder(NativeMethods2.MaxGuidLength + 1);

            if (NativeMethods2.MsiGetShortcutTarget(file, product, feature, component) != 0) return null;

            int pathLength = NativeMethods2.MaxPathLength;
            StringBuilder path = new StringBuilder(pathLength);

            NativeMethods2.InstallState installState = NativeMethods2.MsiGetComponentPath(product.ToString(), component.ToString(), path, ref pathLength);
            if (installState == NativeMethods2.InstallState.Local)
            {
                return path.ToString();
            }
            else
            {
                return null;
            }
        }

        public string ResolveShortcut(string filePath)
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            string result;

            try
            {
                var shortcut = shell.CreateShortcut(filePath);
                result = shortcut.TargetPath;
                Marshal.FinalReleaseComObject(shortcut);
            }
            catch (COMException)
            {
                // A COMException is thrown if the file is not a valid shortcut (.lnk) file 
                result = null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }

            return result;
        }

        public bool MoveItemUpDown(ProgramItem item, int moveDirection)
        {
            int oldIndex = programColl.IndexOf(item);
            int newIndex = oldIndex + moveDirection;
            if (Math.Abs(moveDirection) != 1 || oldIndex < 0 || newIndex < 0 || newIndex >= programColl.Count) return false;
            programColl.Move(oldIndex, newIndex);
            // Unsaved discoveries can be moved too. Saved rules retain the visible relative priority.
            var rules = programColl.Where(row => row.MatchedAutoProfile != null).Select(row => row.MatchedAutoProfile).ToArray();
            for (int i = 0; i < rules.Length; i++)
            {
                int current = autoProfileHolder.AutoProfileColl.IndexOf(rules[i]);
                if (current != i) autoProfileHolder.AutoProfileColl.Move(current, i);
            }
            return true;
        }

        public void AddExeToHIDHideWhenSaving(ProgramItem autoProf, bool addExe)
        {
            if (Path.GetExtension(autoProf.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) //Filter out autoprofiles that do not lead to EXEs.
            {
                if (App.rootHub != null) App.rootHub.CheckHidHidePresence(autoProf.Path, autoProf.Filename, addExe);
            }
        }
    }

    public class ProgramItem
    {
        private string path;
        private string path_lowercase;
        private string filename;
        private string title;
        private string title_lowercase;
        private AutoProfileEntity matchedAutoProfile;

        private bool turnoff;
        private AutoProfileDeviceOption deviceOption = AutoProfileDeviceOption.Any;
        private bool applyToAllControllers;

        public string Path { get => path;
            set
            {
                if (path == value) return;
                path = value ?? string.Empty;
                filename = System.IO.Path.GetFileNameWithoutExtension(path);
                FilenameChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string Title { get => title;
            set
            {
                if (title == value) return;
                title = value;

                TitleChanged?.Invoke(this, EventArgs.Empty);
                FilenameChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler TitleChanged;
        public AutoProfileEntity MatchedAutoProfile
        {
            get => matchedAutoProfile;
            set
            {
                matchedAutoProfile = value;
                if (matchedAutoProfile != null)
                {
                    enabled = matchedAutoProfile.Enabled;
                    turnoff = matchedAutoProfile.Turnoff;
                    title = matchedAutoProfile.Title ?? string.Empty;
                    title_lowercase = title.ToLower();
                    deviceOption = matchedAutoProfile.DeviceOption;
                    applyToAllControllers = matchedAutoProfile.ApplyToAllControllers;
                }

                MatchedAutoProfileChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler MatchedAutoProfileChanged;
        //public delegate void AutoProfileHandler(ProgramItem sender, bool added);
        //public event AutoProfileHandler AutoProfileAction;
        public string Filename
        {
            get
            {
                if (!string.IsNullOrEmpty(filename))
                {
                    return filename;
                }

                return string.IsNullOrEmpty(title) ? Translations.Strings.WindowTitleRule : title;
            }
        }
        public event EventHandler FilenameChanged;
        public ImageSource Exeicon => null;

        private bool enabled = true;
        public bool Enabled { get => enabled; set { enabled = value; EnabledChanged?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler EnabledChanged;
        public string StateText => matchedAutoProfile == null ? "Not saved" : matchedAutoProfile.Enabled ? "Enabled" : "Disabled";
        public event EventHandler StateTextChanged;
        public void RefreshState() => StateTextChanged?.Invoke(this, EventArgs.Empty);

        public bool Turnoff
        {
            get => turnoff;
            set
            {
                turnoff = value;

                TurnoffChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler TurnoffChanged;

        public AutoProfileDeviceOption DeviceOption
        {
            get => deviceOption;
            set
            {
                deviceOption = value;

                DeviceOptionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler DeviceOptionChanged;

        public bool ApplyToAllControllers
        {
            get => applyToAllControllers;
            set
            {
                applyToAllControllers = value;

                ApplyToAllControllersChanged?.Invoke(this, EventArgs.Empty);
                ControllerSpecificProfilesVisibleChanged?.Invoke(this, EventArgs.Empty);
                ExpandedControllerSpecificProfilesVisibleChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ApplyToAllControllersChanged;

        public Visibility ControllerSpecificProfilesVisible
        {
            get => ApplyToAllControllers ? Visibility.Collapsed : Visibility.Visible;
        }
        public event EventHandler ControllerSpecificProfilesVisibleChanged;

        public Visibility ExpandedControllerSpecificProfilesVisible
        {
            get
            {
                if (!ControlService.USING_MAX_CONTROLLERS || ApplyToAllControllers)
                {
                    return Visibility.Collapsed;
                }

                return Visibility.Visible;
            }
        }
        public event EventHandler ExpandedControllerSpecificProfilesVisibleChanged;

        public bool Exists
        {
            get => matchedAutoProfile != null;
        }
        public event EventHandler ExistsChanged;

        private int selectedIndexCon1 = 0;
        private int selectedIndexCon2 = 0;
        private int selectedIndexCon3 = 0;
        private int selectedIndexCon4 = 0;
        private int selectedIndexCon5 = 0;
        private int selectedIndexCon6 = 0;
        private int selectedIndexCon7 = 0;
        private int selectedIndexCon8 = 0;

        public int SelectedIndexCon1
        {
            get => selectedIndexCon1;
            set
            {
                if (selectedIndexCon1 == value) return;
                selectedIndexCon1 = value;
            }
        }

        public int SelectedIndexCon2
        {
            get => selectedIndexCon2;
            set
            {
                if (selectedIndexCon2 == value) return;
                selectedIndexCon2 = value;
            }
        }
        public int SelectedIndexCon3
        {
            get => selectedIndexCon3;
            set
            {
                if (selectedIndexCon3 == value) return;
                selectedIndexCon3 = value;
            }
        }
        public int SelectedIndexCon4
        {
            get => selectedIndexCon4;
            set
            {
                if (selectedIndexCon4 == value) return;
                selectedIndexCon4 = value;
            }
        }

        public int SelectedIndexCon5
        {
            get => selectedIndexCon5;
            set
            {
                if (selectedIndexCon5 == value) return;
                selectedIndexCon5 = value;
            }
        }

        public int SelectedIndexCon6
        {
            get => selectedIndexCon6;
            set
            {
                if (selectedIndexCon6 == value) return;
                selectedIndexCon6 = value;
            }
        }

        public int SelectedIndexCon7
        {
            get => selectedIndexCon7;
            set
            {
                if (selectedIndexCon7 == value) return;
                selectedIndexCon7 = value;
            }
        }

        public int SelectedIndexCon8
        {
            get => selectedIndexCon8;
            set
            {
                if (selectedIndexCon8 == value) return;
                selectedIndexCon8 = value;
            }
        }

        public Visibility ExpandedControllersVisible
        {
            get
            {
                Visibility temp = Visibility.Visible;
                if (!ControlService.USING_MAX_CONTROLLERS)
                {
                    temp = Visibility.Collapsed;
                }

                return temp;
            }
        }

        public ProgramItem(string path, AutoProfileEntity autoProfileEntity = null)
        {
            this.path = path;
            this.path_lowercase = path.ToLower();
            filename = System.IO.Path.GetFileNameWithoutExtension(path);
            this.matchedAutoProfile = autoProfileEntity;
            if (autoProfileEntity != null)
            {
                enabled = autoProfileEntity.Enabled;
                title = autoProfileEntity.Title;
                title_lowercase = title.ToLower();
                turnoff = autoProfileEntity.Turnoff;
                deviceOption = autoProfileEntity.DeviceOption;
                applyToAllControllers = autoProfileEntity.ApplyToAllControllers;
            }

            // Shell icon extraction can block for seconds on a single executable.
            // Discovery rows use the application's neutral icon instead.
            MatchedAutoProfileChanged += ProgramItem_MatchedAutoProfileChanged;
        }

        private void ProgramItem_MatchedAutoProfileChanged(object sender, EventArgs e)
        {
            if (matchedAutoProfile == null)
            {
                selectedIndexCon1 = 0;
                selectedIndexCon2 = 0;
                selectedIndexCon3 = 0;
                selectedIndexCon4 = 0;
                selectedIndexCon5 = 0;
                selectedIndexCon6 = 0;
                selectedIndexCon7 = 0;
                selectedIndexCon8 = 0;
            }

            ExistsChanged?.Invoke(this, EventArgs.Empty);
            RefreshState();
        }
    }

    [SuppressUnmanagedCodeSecurity]
    class NativeMethods2
    {
        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        public static extern uint MsiGetShortcutTarget(string targetFile, StringBuilder productCode, StringBuilder featureID, StringBuilder componentCode);

        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        public static extern InstallState MsiGetComponentPath(string productCode, string componentCode, StringBuilder componentPath, ref int componentPathBufferSize);

        public const int MaxFeatureLength = 38;
        public const int MaxGuidLength = 38;
        public const int MaxPathLength = 1024;

        public enum InstallState
        {
            NotUsed = -7,
            BadConfig = -6,
            Incomplete = -5,
            SourceAbsent = -4,
            MoreData = -3,
            InvalidArg = -2,
            Unknown = -1,
            Broken = 0,
            Advertised = 1,
            Removed = 1,
            Absent = 2,
            Local = 3,
            Source = 4,
            Default = 5
        }
    }
}
