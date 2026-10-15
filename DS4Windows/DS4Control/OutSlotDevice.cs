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
using DS4Windows;

namespace DS4WinWPF.DS4Control
{
    public class OutSlotDevice
    {
        public enum AttachedStatus : uint
        {
            UnAttached = 0,
            Attached = 1,
        }

        public enum ReserveStatus : uint
        {
            Dynamic = 0,
            Permanent = 1,
        }

        public enum InputBound : uint
        {
            Unbound = 0,
            Bound = 1,
        }

        public const int INPUT_INDEX_DEFAULT = -1;
        private AttachedStatus attachedStatus;
        private OutputDevice outputDevice;
        private ReserveStatus reserveStatus;
        private InputBound inputBound;
        private OutContType permanentType;
        private OutContType currentType;
        private int index;
        public int Index => index;

        private int inputIndex = INPUT_INDEX_DEFAULT;
        public int InputIndex
        {
            get => inputIndex;
            set => inputIndex = value;
        }

        private string inputDisplayString = string.Empty;
        public string InputDisplayString
        {
            get => inputDisplayString;
            set => inputDisplayString = value;
        }

        /// <summary>
        /// Connection status of virtual output controller
        /// </summary>
        public AttachedStatus CurrentAttachedStatus { get => attachedStatus; }

        /// <summary>
        /// Reference to output controller
        /// </summary>
        public OutputDevice OutputDevice { get => outputDevice; }

        /// <summary>
        /// Flag stating the connection preference of an output controller
        /// </summary>
        public ReserveStatus CurrentReserveStatus
        {
            get => reserveStatus;
            set
            {
                if (reserveStatus == value) return;
                reserveStatus = value;
                CurrentReserveStatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CurrentReserveStatusChanged;

        /// <summary>
        /// Whether an input controller is associated with the slot
        /// </summary>
        public InputBound CurrentInputBound
        {
            get => inputBound;
            set
            {
                SetInputBindingDeferred(value)?.Invoke();
            }
        }
        public event EventHandler CurrentInputBoundChanged;

        /// <summary>
        /// Desired device type for a permanently connected slot
        /// </summary>
        public OutContType PermanentType
        {
            get => permanentType;
            set
            {
                value = value.Normalize();
                if (permanentType == value) return;

                if(value != OutContType.None)
                    AppLogger.LogToGui($"Output slot #{this.index+1} has permanent type {value}", false);

                permanentType = value;
                PermanentTypeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler PermanentTypeChanged;

        /// <summary>
        /// Device type of the current output controller
        /// </summary>
        public OutContType CurrentType
        {
            get => currentType;
            set => currentType = value.Normalize();
        }

        public OutSlotDevice(int idx)
        {
            this.index = idx;
            CurrentReserveStatusChanged += OutSlotDevice_CurrentReserveStatusChanged;
        }

        private void OutSlotDevice_CurrentReserveStatusChanged(object sender, EventArgs e)
        {
            if (reserveStatus == ReserveStatus.Dynamic)
            {
                PermanentType = OutContType.None;
            }
            else if (currentType != OutContType.None)
            {
                PermanentType = currentType;
            }
        }

        public void AttachedDevice(OutputDevice outputDevice, OutContType contType, int inIdx, string inDisplayString)
        {
            this.outputDevice = outputDevice;
            attachedStatus = AttachedStatus.Attached;
            currentType = contType.Normalize();
            inputIndex = inIdx;
            inputDisplayString = inDisplayString;
            //desiredType = contType;
        }

        public void DetachDevice()
        {
            DetachDeviceDeferred()?.Invoke();
        }

        // The manager commits state while locked, then publishes these callbacks
        // after unlocking. Snapshot subscribers at commit time, not dispatch time.
        internal Action SetInputBindingDeferred(InputBound value)
        {
            if (inputBound == value) return null;
            inputBound = value;
            EventHandler handlers = CurrentInputBoundChanged;
            return handlers == null ? null : () => NotifyObservers(handlers, "input binding");
        }

        internal Action DetachDeviceDeferred()
        {
            if (outputDevice == null) return null;

            // Commit the whole detached state before notifying bindings. A UI
            // observer must not leave a half-detached slot if it throws while
            // reacting to the property change.
            outputDevice = null;
            attachedStatus = AttachedStatus.UnAttached;
            currentType = OutContType.None;
            bool inputBindingChanged = inputBound != InputBound.Unbound;
            inputBound = InputBound.Unbound;
            bool permanentTypeChanged = reserveStatus == ReserveStatus.Dynamic &&
                permanentType != OutContType.None;
            if (permanentTypeChanged) permanentType = OutContType.None;
            inputIndex = INPUT_INDEX_DEFAULT;
            inputDisplayString = string.Empty;

            EventHandler bindingHandlers = inputBindingChanged ? CurrentInputBoundChanged : null;
            EventHandler typeHandlers = permanentTypeChanged ? PermanentTypeChanged : null;
            if (bindingHandlers == null && typeHandlers == null) return null;
            return () =>
            {
                NotifyObservers(bindingHandlers, "input binding");
                NotifyObservers(typeHandlers, "permanent type");
            };
        }

        private void NotifyObservers(EventHandler handlers, string kind)
        {
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception error)
                {
                    ControlService.StartupDiag($"OutSlotDevice {kind} observer failed for slot #{index + 1}: {error}");
                }
            }
        }

        ~OutSlotDevice()
        {
            DetachDevice();
        }
    }
}
