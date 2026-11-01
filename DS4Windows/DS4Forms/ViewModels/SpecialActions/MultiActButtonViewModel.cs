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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels.Util;

namespace DS4WinWPF.DS4Forms.ViewModels.SpecialActions
{
    public class MultiActButtonViewModel : NotifyDataErrorBase, INotifyPropertyChanged
    {
        private List<int> tapMacro = new List<int>();
        private List<int> holdMacro = new List<int>();
        private List<int> doubleTapMacro = new List<int>();
        private List<int>[] loadAccessArray;
        public List<int> TripleTapMacro { get; } = new();
        public double HoldSeconds { get; set; } = 0.5;
        public double TapSeconds { get; set; } = 0.15;
        private readonly bool[] enabled = { true, true, true, true };
        public event PropertyChangedEventHandler PropertyChanged;
        private void SetEnabled(int index, bool value, [CallerMemberName] string property = null)
        {
            if (enabled[index] == value) return;
            enabled[index] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
        public bool TapEnabled { get => enabled[0]; set => SetEnabled(0, value); }
        public bool HoldEnabled { get => enabled[1]; set => SetEnabled(1, value); }
        public bool DoubleTapEnabled { get => enabled[2]; set => SetEnabled(2, value); }
        public bool TripleTapEnabled { get => enabled[3]; set => SetEnabled(3, value); }
        public string TapName { get; set; } = "";
        public string HoldName { get; set; } = "";
        public string DoubleTapName { get; set; } = "";
        public string TripleTapName { get; set; } = "";
        public string TripleTapMacroText => TripleTapMacro.Count == 0 ? Properties.Resources.SelectMacro : Properties.Resources.MacroRecorded;
        public event EventHandler TripleTapMacroTextChanged;
        public void UpdateTripleTapDisplayText() => TripleTapMacroTextChanged?.Invoke(this, EventArgs.Empty);
        public DS4ControlSettings PrepareTripleTapSettings()
        {
            var settings = new DS4ControlSettings(DS4Controls.None);
            settings.action.actionMacro = TripleTapMacro.ToArray();
            settings.actionType = DS4ControlSettings.ActionType.Macro;
            settings.keyType = DS4KeyType.Macro;
            return settings;
        }

        public List<int> TapMacro { get => tapMacro; }
        public List<int> HoldMacro { get => holdMacro; }
        public List<int> DoubleTapMacro { get => doubleTapMacro; }

        public string TapMacroText
        {
            get
            {
                string result = Properties.Resources.SelectMacro;
                if (tapMacro.Count > 0)
                {
                    result = Properties.Resources.MacroRecorded;
                }

                return result;
            }
        }
        public event EventHandler TapMacroTextChanged;

        public string HoldMacroText
        {
            get
            {
                string result = Properties.Resources.SelectMacro;
                if (holdMacro.Count > 0)
                {
                    result = Properties.Resources.MacroRecorded;
                }

                return result;
            }
        }
        public event EventHandler HoldMacroTextChanged;

        public string DoubleTapMacroText
        {
            get
            {
                string result = Properties.Resources.SelectMacro;
                if (doubleTapMacro.Count > 0)
                {
                    result = Properties.Resources.MacroRecorded;
                }

                return result;
            }
        }
        public event EventHandler DoubleTapMacroTextChanged;

        public MultiActButtonViewModel()
        {
            loadAccessArray = new List<int>[] { tapMacro, holdMacro, doubleTapMacro, TripleTapMacro };
        }

        public void LoadAction(SpecialAction action)
        {
            var options = new MultiActionConfiguration(action.details);
            HoldSeconds = options.HoldMilliseconds / 1000.0;
            TapSeconds = options.TapMilliseconds / 1000.0;
            TapEnabled = options.Enabled[0]; HoldEnabled = options.Enabled[1];
            DoubleTapEnabled = options.Enabled[2]; TripleTapEnabled = options.Enabled[3];
            TapName = options.Names[0] ?? ""; HoldName = options.Names[1] ?? "";
            DoubleTapName = options.Names[2] ?? ""; TripleTapName = options.Names[3] ?? "";
            string[] dets = options.Macros;
            foreach (var macro in loadAccessArray) macro.Clear();
            for (int i = 0; i < 4; i++)
            {
                string[] macs = dets[i].Split('/');
                foreach (string s in macs)
                {
                    if (int.TryParse(s, out int v))
                        loadAccessArray[i].Add(v);
                }
            }
        }

        public void UpdateTapDisplayText()
        {
            TapMacroTextChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateHoldDisplayText()
        {
            HoldMacroTextChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateDoubleTapDisplayText()
        {
            DoubleTapMacroTextChanged?.Invoke(this, EventArgs.Empty);
        }

        public DS4ControlSettings PrepareTapSettings()
        {
            DS4ControlSettings settings = new DS4ControlSettings(DS4Controls.None);
            settings.action.actionMacro = tapMacro.ToArray();
            settings.actionType = DS4ControlSettings.ActionType.Macro;
            settings.keyType = DS4KeyType.Macro;
            return settings;
        }

        public DS4ControlSettings PrepareHoldSettings()
        {
            DS4ControlSettings settings = new DS4ControlSettings(DS4Controls.None);
            settings.action.actionMacro = holdMacro.ToArray();
            settings.actionType = DS4ControlSettings.ActionType.Macro;
            settings.keyType = DS4KeyType.Macro;
            return settings;
        }

        public DS4ControlSettings PrepareDoubleTapSettings()
        {
            DS4ControlSettings settings = new DS4ControlSettings(DS4Controls.None);
            settings.action.actionMacro = doubleTapMacro.ToArray();
            settings.actionType = DS4ControlSettings.ActionType.Macro;
            settings.keyType = DS4KeyType.Macro;
            return settings;
        }

        public void SaveAction(SpecialAction action, bool edit = false)
        {
            Global.SaveAction(action.name, action.controls, 7, SerializeDetails(), edit);
        }

        internal string SerializeDetails() => MultiActionConfiguration.Serialize(
            loadAccessArray.Select(macro => string.Join("/", macro)).ToArray(), enabled,
            new[] { TapName, HoldName, DoubleTapName, TripleTapName },
            (int)Math.Round(HoldSeconds * 1000), (int)Math.Round(TapSeconds * 1000));

        public override bool IsValid(SpecialAction action)
        {
            ClearOldErrors();

            bool valid = TapMacro.Count + HoldMacro.Count + DoubleTapMacro.Count + TripleTapMacro.Count > 0;
            if (new[] { TapName, HoldName, DoubleTapName, TripleTapName }.Any(name => name?.Length > 128))
            { valid = false; errors["TapMacro"] = new List<string> { "Macro names must be at most 128 characters." }; RaiseErrorsChanged("TapMacro"); }
            if (!valid) { errors["TapMacro"] = new List<string> { "Record at least one macro." }; RaiseErrorsChanged("TapMacro"); }
            if (!double.IsFinite(HoldSeconds) || HoldSeconds < 0.15 || HoldSeconds > 60)
            { valid = false; errors["HoldSeconds"] = new List<string> { "Use 0.15 to 60 seconds." }; RaiseErrorsChanged("HoldSeconds"); }
            if (!double.IsFinite(TapSeconds) || TapSeconds < 0.05 || TapSeconds > 2 || TapSeconds > HoldSeconds)
            { valid = false; errors["TapSeconds"] = new List<string> { "Use 0.05 to 2 seconds, no longer than the hold duration." }; RaiseErrorsChanged("TapSeconds"); }
            return valid;
        }

        public override void ClearOldErrors()
        {
            if (errors.Count > 0)
            {
                errors.Clear();
                RaiseErrorsChanged("TapMacro");
                RaiseErrorsChanged("HoldMacro");
                RaiseErrorsChanged("DoubleTapMacro");
                RaiseErrorsChanged("HoldSeconds");
                RaiseErrorsChanged("TapSeconds");
            }
        }
    }
}
