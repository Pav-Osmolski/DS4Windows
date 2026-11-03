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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.ViewModels
{
    public class SpecialActionsListViewModel
    {
        private int deviceNum;
        private ObservableCollection<SpecialActionItem> actionCol =
            new ObservableCollection<SpecialActionItem>();
        private int specialActionIndex = -1;
        private readonly LatestPreferenceWriter<string[]> orderWriter;
        internal Func<SettingsStorage> Storage = () => SettingsStorage.Current;
        internal Task OrderSaved => orderWriter.Saved;
        public bool CanMoveUp => currentSAItem != null && actionCol.IndexOf(currentSAItem) > 0;
        public bool CanMoveDown => currentSAItem != null && actionCol.IndexOf(currentSAItem) is int index && index >= 0 && index < actionCol.Count - 1;
        public event EventHandler CanMoveUpChanged;
        public event EventHandler CanMoveDownChanged;
        private void RefreshMoveState()
        {
            CanMoveUpChanged?.Invoke(this, EventArgs.Empty);
            CanMoveDownChanged?.Invoke(this, EventArgs.Empty);
        }

        public ObservableCollection<SpecialActionItem> ActionCol { get => actionCol; }
        public int DeviceNum { get => deviceNum; }
        public int SpecialActionIndex
        {
            get => specialActionIndex;
            set
            {
                if (specialActionIndex == value) return;
                specialActionIndex = value;
                SpecialActionIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler SpecialActionIndexChanged;

        private SpecialActionItem currentSAItem;
        public SpecialActionItem CurrentSpecialActionItem
        {
            get => currentSAItem;
            set
            {
                currentSAItem = value;
                // Selection follows the action itself, never a possibly sorted
                // view index. Reordering must not select a different action.
                SpecialActionIndex = actionCol.IndexOf(value);
                RefreshMoveState();
            }
        }

        public bool ItemSelected { get => specialActionIndex >= 0; }
        public event EventHandler ItemSelectedChanged;

        public SpecialActionsListViewModel(int deviceNum)
        {
            this.deviceNum = deviceNum;
            orderWriter = new(order => Storage().SetSpecialActionOrder(order),
                error => NLog.LogManager.GetLogger("SpecialActions").Warn(error, "Could not remember action display order."));

            SpecialActionIndexChanged += SpecialActionsListViewModel_SpecialActionIndexChanged;
            actionCol.CollectionChanged += ActionCol_CollectionChanged;
        }

        private void ActionCol_CollectionChanged(object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or
                System.Collections.Specialized.NotifyCollectionChangedAction.Move or
                System.Collections.Specialized.NotifyCollectionChangedAction.Add or
                System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                for (int i = 0; i < actionCol.Count; i++)
                {
                    // Replace old index with updated index
                    actionCol[i].Index = i;
                }
            }
            RefreshMoveState();
        }

        private void SpecialActionsListViewModel_SpecialActionIndexChanged(object sender, EventArgs e)
        {
            ItemSelectedChanged?.Invoke(this, EventArgs.Empty);
            RefreshMoveState();
        }

        public void LoadActions(bool newProfile = false)
            => LoadActions(Global.GetActions(), newProfile);

        internal void LoadActions(IEnumerable<SpecialAction> actions, bool newProfile = false)
        {
            actionCol.Clear();

            List<string> pactions = Global.ProfileActions[deviceNum];
            int idx = 0;
            foreach (SpecialAction action in actions)
            {
                string displayName = GetActionDisplayName(action);
                SpecialActionItem item = new SpecialActionItem(action, displayName, idx);

                if (pactions.Contains(action.name))
                {
                    item.Active = true;
                }
                else if (newProfile && action.typeID == SpecialAction.ActionTypeId.DisconnectBT)
                {
                    item.Active = true;
                }

                actionCol.Add(item);
                idx++;
            }
            try
            {
                var order = Storage().SpecialActionOrder;
                var ranks = order.Select((name, rank) => (name, rank)).ToDictionary(x => x.name, x => x.rank, StringComparer.Ordinal);
                Arrange(actionCol.OrderBy(item => ranks.TryGetValue(item.ActionName, out int rank) ? rank : int.MaxValue).ToArray());
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { NLog.LogManager.GetLogger("SpecialActions").Warn(error, "Could not read action display order."); }
        }

        private void Arrange(SpecialActionItem[] desired)
        {
            var selected = currentSAItem;
            for (int i = 0; i < desired.Length; i++)
            {
                int current = actionCol.IndexOf(desired[i]);
                if (current != i) actionCol.Move(current, i);
            }
            CurrentSpecialActionItem = actionCol.Contains(selected) ? selected : null;
            SpecialActionIndex = actionCol.IndexOf(CurrentSpecialActionItem);
        }

        public void MoveSelectedAction(int direction)
        {
            int index = actionCol.IndexOf(currentSAItem);
            int target = index + Math.Sign(direction);
            if (direction == 0 || index < 0 || target < 0 || target >= actionCol.Count) return;
            var selected = currentSAItem;
            actionCol.Move(index, target);
            CurrentSpecialActionItem = selected;
            SpecialActionIndex = target;
            RememberOrder();
        }

        public void SortActions(string property, bool descending)
        {
            Func<SpecialActionItem, string> key = property switch
            {
                nameof(SpecialActionItem.ActionName) => item => item.ActionName,
                nameof(SpecialActionItem.Controls) => item => item.Controls,
                nameof(SpecialActionItem.TypeName) => item => item.TypeName,
                _ => null,
            };
            if (key == null) return;
            Arrange((descending ? actionCol.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase) :
                actionCol.OrderBy(key, StringComparer.CurrentCultureIgnoreCase)).ToArray());
            RememberOrder();
        }

        internal void RememberOrder() => orderWriter.Queue(actionCol.Select(item => item.ActionName).ToArray());

        public SpecialActionItem CreateActionItem(SpecialAction action)
        {
            string displayName = GetActionDisplayName(action);
            SpecialActionItem item = new SpecialActionItem(action, displayName, 0);
            return item;
        }

        public string GetActionDisplayName(SpecialAction action)
        {
            string displayName = string.Empty;
            switch (action.typeID)
            {
                case SpecialAction.ActionTypeId.DisconnectBT:
                    displayName = Properties.Resources.DisconnectBT; break;
                case SpecialAction.ActionTypeId.Macro:
                    displayName = Properties.Resources.Macro + (action.keyType.HasFlag(DS4KeyType.ScanCode) ? " (" + Properties.Resources.ScanCode + ")" : "");
                    break;
                case SpecialAction.ActionTypeId.Program:
                    displayName = Properties.Resources.LaunchProgram.Replace("*program*", Path.GetFileName(action.details));
                    break;
                case SpecialAction.ActionTypeId.Profile:
                    displayName = Properties.Resources.LoadProfile.Replace("*profile*", action.details);
                    break;
                case SpecialAction.ActionTypeId.Key:
                    displayName = KeyInterop.KeyFromVirtualKey(int.Parse(action.details)).ToString() +
                         (action.keyType.HasFlag(DS4KeyType.Toggle) ? " (Toggle)" : "");
                    break;
                case SpecialAction.ActionTypeId.BatteryCheck:
                    displayName = Properties.Resources.CheckBattery;
                    break;
                case SpecialAction.ActionTypeId.XboxGameDVR:
                    displayName = "Xbox Game DVR";
                    break;
                case SpecialAction.ActionTypeId.MultiAction:
                    displayName = Properties.Resources.MultiAction;
                    break;
                case SpecialAction.ActionTypeId.SASteeringWheelEmulationCalibrate:
                    displayName = Properties.Resources.SASteeringWheelEmulationCalibrate;
                    break;
                case SpecialAction.ActionTypeId.GyroCalibrate:
                    displayName = Translations.Strings.SpecialActionEdit_CalibrateGyro;
                    break;
                default: break;
            }

            return displayName;
        }

        public void ExportEnabledActions()
        {
            List<string> pactions = new List<string>();
            foreach(SpecialActionItem item in actionCol)
            {
                if (item.Active)
                {
                    pactions.Add(item.ActionName);
                }
            }

            // Display sorting must not change action execution priority. Keep
            // existing enabled order and append only newly enabled actions in
            // the stable global action order.
            var enabled = pactions.ToHashSet(StringComparer.Ordinal);
            Global.ProfileActions[deviceNum] = Global.ProfileActions[deviceNum]
                .Concat(Global.GetActions().Select(action => action.name))
                .Concat(pactions)
                .Where(enabled.Contains).Distinct(StringComparer.Ordinal).ToList();
            Global.CacheExtraProfileInfo(deviceNum);
        }

        public void RemoveAction(SpecialActionItem item)
        {
            Global.RemoveAction(item.SpecialAction.name);
            actionCol.Remove(item);
            RememberOrder();
            Global.ProfileActions[deviceNum].Remove(item.SpecialAction.name);
            Global.CacheExtraProfileInfo(deviceNum);
        }
    }

    public class SpecialActionItem
    {
        private SpecialAction specialAction;
        private bool active;
        private string typeName;
        private int index = 0;

        public SpecialActionItem(SpecialAction specialAction, string displayName,
            int index)
        {
            this.specialAction = specialAction;
            this.typeName = displayName;
            this.index = index;
        }

        /// <summary>
        /// Index of SpecialActionItem in the ObservableCollection
        /// </summary>
        public int Index
        {
            get => index;
            set => index = value;
        }

        /// <summary>
        /// The user defined name for a Special Action
        /// </summary>
        public string ActionName
        {
            get => specialAction.name;
            set
            {
                specialAction.name = value;
            }
        }
        public event EventHandler ActionNameChanged;

        /// <summary>
        /// Flag to determine if a Special Action is enabled in a specific Profile
        /// </summary>
        public bool Active
        {
            get => active;
            set
            {
                if (active == value) return;
                active = value;
                ActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ActiveChanged;

        /// <summary>
        /// Display string with the trigger controls that launch a Special Action
        /// </summary>
        public string Controls { get => specialAction.controls.Replace("/", ", "); }

        public event EventHandler ControlsChanged;

        /// <summary>
        /// Cached display string for the base type of the Special Action
        /// </summary>
        public string TypeName { get => typeName; }

        /// <summary>
        /// Reference to the SpecialAction instance
        /// </summary>
        public SpecialAction SpecialAction { get => specialAction; }

        public void Refresh()
        {
            ActionNameChanged?.Invoke(this, EventArgs.Empty);
            ControlsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
