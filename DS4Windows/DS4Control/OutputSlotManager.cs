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
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DS4WinWPF.DS4Control;

namespace DS4Windows
{
    public class OutputSlotManager
    {
        public const int DELAY_TIME = 500; // measured in ms
        private OutSlotDevice[] outputSlots;/* = new OutSlotDevice[Global.MAX_DS4_CONTROLLER_COUNT]
        {
            new OutSlotDevice(0), new OutSlotDevice(1),
            new OutSlotDevice(2), new OutSlotDevice(3)
        };
        */
        private int lastSlotIndex;

        public int NumAttachedDevices
        {
            get
            {
                using ReadLocker locker = new ReadLocker(queueLocker);
                int result = 0;
                for (int i = 0; i < outputSlots.Length; i++)
                {
                    OutSlotDevice tmp = outputSlots[i];
                    if (tmp.CurrentAttachedStatus == OutSlotDevice.AttachedStatus.Attached)
                    {
                        result++;
                    }
                }

                return result;
            }
        }

        private Dictionary<int, OutputDevice> deviceDict = new Dictionary<int, OutputDevice>();
        private Dictionary<OutputDevice, int> revDeviceDict = new Dictionary<OutputDevice, int>();
        private OutputDevice[] outputDevices = new OutputDevice[ControlService.CURRENT_DS4_CONTROLLER_LIMIT];

        private int queuedTasks = 0;
        private ReaderWriterLockSlim queueLocker;
        private readonly ConcurrentQueue<Action> notifications = new ConcurrentQueue<Action>();
        private int drainingNotifications;
        private int stopping;
        private readonly Action<IReadOnlyCollection<string>>
            virtualSonyRegisteredCallback;
        private readonly Func<OutContType, OutputDevice> outputFactory;

        public bool RunningQueue { get => queuedTasks > 0; }
        public OutSlotDevice[] OutputSlots { get => outputSlots; }

        public delegate void SlotAssignedDelegate(OutputSlotManager sender,
            int slotNum, OutSlotDevice outSlotDev);
        public event SlotAssignedDelegate SlotAssigned;

        public delegate void SlotUnassignedDelegate(OutputSlotManager sender,
            int slotNum, OutSlotDevice outSlotDev);
        public event SlotUnassignedDelegate SlotUnassigned;

        public OutputSlotManager(
            Action<IReadOnlyCollection<string>> virtualSonyRegisteredCallback = null)
            : this(virtualSonyRegisteredCallback, null)
        {
        }

        internal OutputSlotManager(
            Action<IReadOnlyCollection<string>> virtualSonyRegisteredCallback,
            Func<OutContType, OutputDevice> outputFactory)
        {
            this.virtualSonyRegisteredCallback = virtualSonyRegisteredCallback;
            this.outputFactory = outputFactory;
            outputSlots = new OutSlotDevice[ControlService.CURRENT_DS4_CONTROLLER_LIMIT];
            for (int i = 0; i < ControlService.CURRENT_DS4_CONTROLLER_LIMIT; i++)
            {
                outputSlots[i] = new OutSlotDevice(i);
            }

            lastSlotIndex = outputSlots.Length > 0 ? outputSlots.Length - 1 : 0;

            queueLocker = new ReaderWriterLockSlim();
        }

        public void ShutDown()
        {
        }

        public void Stop(bool immediate = false)
        {
            Interlocked.Increment(ref stopping);
            try
            {
                UnplugRemainingControllers(immediate);
                Stopwatch queueWait = Stopwatch.StartNew();
                while (RunningQueue && queueWait.ElapsedMilliseconds < 2000)
                {
                    Thread.Sleep(1);
                }

                if (RunningQueue)
                {
                    ControlService.StartupDiag("OutputSlotManager.Stop timed out waiting for queued output task");
                }

                using (WriteLocker locker = new WriteLocker(queueLocker))
                {
                    deviceDict.Clear();
                    revDeviceDict.Clear();
                }
            }
            finally { Interlocked.Decrement(ref stopping); }
        }

        public OutputDevice AllocateController(OutContType contType)
        {
            contType = contType.Normalize();
            if (outputFactory != null) return outputFactory(contType);
            OutputDevice outputDevice = null;
            switch (contType)
            {
                case OutContType.ViiperX360:
                    outputDevice = new ViiperOutDevice(contType, ViiperVirtualDeviceType.Xbox360);
                    break;
                case OutContType.ViiperDS4:
                    outputDevice = new ViiperOutDevice(contType,
                        ViiperVirtualDeviceType.DualShock4);
                    break;
                case OutContType.ViiperDualSense:
                    outputDevice = new ViiperOutDevice(contType,
                        ViiperVirtualDeviceType.DualSense);
                    break;
                case OutContType.ViiperDualSenseEdge:
                    outputDevice = new ViiperOutDevice(contType,
                        ViiperVirtualDeviceType.DualSenseEdge);
                    break;
                case OutContType.ViiperSwitch2Pro:
                    outputDevice = new ViiperOutDevice(contType, ViiperVirtualDeviceType.Switch2Pro);
                    break;
                case OutContType.ViiperXboxOne:
                    outputDevice = new ViiperOutDevice(contType,
                        ViiperVirtualDeviceType.XboxOne);
                    break;
                case OutContType.None:
                default:
                    break;
            }

            return outputDevice;
        }

        private int FindEmptySlot()
        {
            int result = -1;
            for (int i = 0; i < outputDevices.Length && result == -1; i++)
            {
                OutputDevice tempdev = outputDevices[i];
                if (tempdev == null)
                {
                    result = i;
                }
            }

            return result;
        }

        public OutSlotDevice DeferredPlugin(OutputDevice outputDevice, int inIdx, string inDisplayString,
            OutputDevice[] outdevs, OutContType contType, OutSlotDevice preferredSlot = null)
        {
            contType = contType.Normalize();
            ControlService.StartupDiag($"OutputSlotManager.DeferredPlugin enter inIdx={inIdx} contType={contType} outputNull={outputDevice == null}");
            OutSlotDevice assignedSlot = null;
            Action bindingNotification = null;
            using (WriteLocker locker = new WriteLocker(queueLocker))
            {
                if (Volatile.Read(ref stopping) != 0) return null;
                int slot = preferredSlot == null ? FindEmptySlot() :
                    (uint)preferredSlot.Index < outputSlots.Length &&
                    ReferenceEquals(outputSlots[preferredSlot.Index], preferredSlot) &&
                    preferredSlot.CurrentInputBound == OutSlotDevice.InputBound.Unbound &&
                    preferredSlot.CurrentAttachedStatus == OutSlotDevice.AttachedStatus.UnAttached ?
                        preferredSlot.Index : -1;
                ControlService.StartupDiag($"OutputSlotManager.DeferredPlugin emptySlot={slot + 1} inIdx={inIdx} contType={contType}");
                if (slot != -1)
                {
                    // Record every VIIPER Sony output so its complete USB/IP
                    // HID cannot be re-ingested as a physical input.
                    HashSet<string> beforeVirtualSony = null;
                    if (contType == OutContType.ViiperDS4 ||
                        contType == OutContType.ViiperDualSense ||
                        contType == OutContType.ViiperDualSenseEdge)
                    {
                        beforeVirtualSony = DS4Devices.
                            SnapshotBeforeOwnVirtualSony();
                        DS4Devices.BeginOwnVirtualSonyConnect();
                    }

                    try
                    {
                        if (inIdx >= 0 && outputDevice is
                                ViiperOutDevice viiperOutput)
                        {
                            // Xbox CFBK target generations are immutable API
                            // creation fields. Publish the exact physical slot
                            // before Connect opens the VIIPER stream so a
                            // Switch 2 runtime can bind that request without a
                            // later mutable routing guess.
                            viiperOutput.BindPhysicalController(inIdx);
                        }
                        ControlService.StartupDiag($"OutputSlotManager.Connect begin slot={slot + 1} type={contType} output={outputDevice.GetType().Name}");
                        outputDevice.Connect();
                        ControlService.StartupDiag($"OutputSlotManager.Connect end slot={slot + 1} type={contType}");
                    }
                    catch (Win32Exception e)
                    {
                        ControlService.StartupDiag($"OutputSlotManager.Connect Win32Exception slot={slot + 1} type={contType} error={e.ErrorCode} message={e.Message}");
                        // Leave task immediately if connect call failed
                        //queuedTasks--;
                        AppLogger.LogToGui($"Failed to plug in virtual {contType.ToDisplayName()} controller: {e.Message}", true);

                        if (beforeVirtualSony != null)
                        {
                            DS4Devices.EndOwnVirtualSonyConnect();
                        }

                        return null;
                    }
                    catch (Exception e)
                    {
                        AppLogger.LogToGui($"Failed to plug in virtual {contType.ToDisplayName()} controller: {e.Message}", true);
                        if (beforeVirtualSony != null)
                        {
                            DS4Devices.EndOwnVirtualSonyConnect();
                        }

                        return null;
                    }

                    if (beforeVirtualSony != null)
                    {
                        DS4Devices.RegisterOwnVirtualSonyAsync(
                            beforeVirtualSony, virtualSonyRegisteredCallback);
                        // The asynchronous registration worker now owns the
                        // matching EndOwnVirtualSonyConnect call.
                        beforeVirtualSony = null;
                    }

                    AppLogger.LogToGui($"Plugging in virtual {contType.ToDisplayName()} Controller in output slot #{slot + 1}", false);

                    outputDevices[slot] = outputDevice;
                    deviceDict.Add(slot, outputDevice);
                    revDeviceDict.Add(outputDevice, slot);
                    outputSlots[slot].AttachedDevice(outputDevice, contType, inIdx, inDisplayString);
                    if (inIdx != -1)
                    {
                        outdevs[inIdx] = outputDevice;
                        bindingNotification = outputSlots[slot].SetInputBindingDeferred(OutSlotDevice.InputBound.Bound);
                    }
                    ControlService.StartupDiag($"OutputSlotManager.DeferredPlugin assigned slot={slot + 1} inIdx={inIdx} type={contType}");
                    // A pre-Connect FindOpenSlot result is only a hint. Return
                    // the exact slot committed while this write lock is held.
                    assignedSlot = outputSlots[slot];
                    QueueNotification(bindingNotification, slot, assignedSlot, assigned: true);
                }
                else
                {
                    ControlService.StartupDiag($"OutputSlotManager.DeferredPlugin no empty slot inIdx={inIdx} type={contType}");
                }
            }

            DrainNotifications();

            //queuedTasks--;
            return assignedSlot;
        }

        public void DeferredRemoval(OutputDevice outputDevice, int inIdx,
            OutputDevice[] outdevs, bool immediate = false)
        {
            _ = immediate;
            ControlService.StartupDiag($"OutputSlotManager.DeferredRemoval enter inIdx={inIdx} outputNull={outputDevice == null}");
            int removedSlot = -1;
            OutSlotDevice removedSlotDevice = null;
            OutContType removedType = OutContType.None;
            using (WriteLocker locker = new WriteLocker(queueLocker))
            {
                //queuedTasks++;
                if (outputDevice != null &&
                    revDeviceDict.TryGetValue(outputDevice, out int slot) &&
                    (uint)slot < outputSlots.Length &&
                    ReferenceEquals(outputSlots[slot].OutputDevice, outputDevice) &&
                    ReferenceEquals(outputDevices[slot], outputDevice) &&
                    deviceDict.TryGetValue(slot, out OutputDevice registered) &&
                    ReferenceEquals(registered, outputDevice))
                {
                    ControlService.StartupDiag($"OutputSlotManager.DeferredRemoval found slot={slot + 1} type={outputDevice.GetDeviceType()}");
                    //int slot = revDeviceDict[outputDevice];
                    ControlService.StartupDiag($"OutputSlotManager.RemoveFeedbacks begin slot={slot + 1}");
                    outputDevice.RemoveFeedbacks();
                    ControlService.StartupDiag($"OutputSlotManager.RemoveFeedbacks end slot={slot + 1}");
                    ControlService.StartupDiag($"OutputSlotManager.Disconnect begin slot={slot + 1}");
                    outputDevice.Disconnect();
                    ControlService.StartupDiag($"OutputSlotManager.Disconnect end slot={slot + 1}");

                    // Failed teardown must retain the exact registration so
                    // this slot cannot be reused and removal can be retried.
                    outputDevices[slot] = null;
                    deviceDict.Remove(slot);
                    revDeviceDict.Remove(outputDevice);

                    if (outdevs != null && (uint)inIdx < outdevs.Length &&
                        ReferenceEquals(outdevs[inIdx], outputDevice))
                    {
                        outdevs[inIdx] = null;
                    }

                    removedType = outputSlots[slot].CurrentType;
                    removedSlot = slot;
                    removedSlotDevice = outputSlots[slot];
                    QueueNotification(removedSlotDevice.DetachDeviceDeferred(), slot, removedSlotDevice);

                    //if (!immediate)
                    //{
                        //    Task.Delay(DELAY_TIME).Wait();
                    //}
                }
                else
                {
                    ControlService.StartupDiag("OutputSlotManager.DeferredRemoval output not found in reverse map");
                }
            }

            // UI observers can query the slot manager or dispatch work back to
            // the service. Never call them under the slot-map writer lock.
            if (removedSlot >= 0)
            {
                DrainNotifications();

                AppLogger.LogToGui($"Unplugging virtual {removedType.ToDisplayName()} Controller from output slot #{removedSlot + 1}", false);
                ControlService.StartupDiag($"OutputSlotManager.DeferredRemoval unassigned slot={removedSlot + 1}");
            }

            //queuedTasks--;
        }

        public OutSlotDevice FindOpenSlot()
        {
            using ReadLocker locker = new ReadLocker(queueLocker);
            OutSlotDevice temp = null;
            for (int i = 0; i < outputSlots.Length; i++)
            {
                OutSlotDevice tmp = outputSlots[i];
                if (tmp.CurrentInputBound == OutSlotDevice.InputBound.Unbound &&
                    tmp.CurrentAttachedStatus == OutSlotDevice.AttachedStatus.UnAttached)
                {
                    temp = tmp;
                    break;
                }
            }

            return temp;
        }

        public bool SlotAvailable(int slotNum)
        {
            bool result;
            if (slotNum < 0 || slotNum > lastSlotIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(slotNum), "Invalid slot number");
            }

            using ReadLocker locker = new ReadLocker(queueLocker);
            //slotNum -= 1;
            result = outputSlots[slotNum].CurrentAttachedStatus == OutSlotDevice.AttachedStatus.UnAttached;
            return result;
        }

        public OutSlotDevice GetOutSlotDevice(int slotNum)
        {
            OutSlotDevice temp;
            if (slotNum < 0 || slotNum > lastSlotIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(slotNum), "Invalid slot number");
            }

            using ReadLocker locker = new ReadLocker(queueLocker);
            //slotNum -= 1;
            temp = outputSlots[slotNum];
            return temp;
        }

        public OutSlotDevice GetOutSlotDevice(OutputDevice outputDevice)
        {
            OutSlotDevice temp = null;
            using ReadLocker locker = new ReadLocker(queueLocker);
            if (outputDevice != null &&
                revDeviceDict.TryGetValue(outputDevice, out int slotNum))
            {
                temp = outputSlots[slotNum];
            }

            return temp;
        }

        public OutSlotDevice FindExistUnboundSlotType(OutContType contType)
        {
            OutSlotDevice temp = null;
            using ReadLocker locker = new ReadLocker(queueLocker);
            string devtype = contType.ToString();
            for (int i = 0; i < outputSlots.Length; i++)
            {
                OutSlotDevice tmp = outputSlots[i];
                if (tmp.CurrentInputBound == OutSlotDevice.InputBound.Unbound &&
                    (tmp.CurrentAttachedStatus == OutSlotDevice.AttachedStatus.Attached &&
                    (tmp.OutputDevice != null && tmp.OutputDevice.GetDeviceType() == devtype)))
                {
                    temp = tmp;
                    break;
                }
            }

            return temp;
        }

        /// <summary>
        /// Cold-path reservation of an already attached, unbound output. The
        /// candidate is only a hint until it is revalidated under the manager's
        /// write lock; two input lifetimes cannot both adopt the same output.
        /// This does not recreate or change an output's immutable feedback target.
        /// </summary>
        internal bool TryBindExistingUnboundOutput(OutSlotDevice candidate,
            OutputDevice[] boundOutputs, int inputIndex, string inputDisplayString,
            OutContType expectedType, out OutputDevice produced)
        {
            produced = null;
            if (candidate == null || boundOutputs == null ||
                (uint)inputIndex >= boundOutputs.Length)
                return false;
            expectedType = expectedType.Normalize();
            using (WriteLocker locker = new WriteLocker(queueLocker))
            {
                if (Volatile.Read(ref stopping) != 0) return false;
                OutputDevice output = candidate.OutputDevice;
                if (!IsExactAttachedSlotNoLock(candidate, output) ||
                    candidate.CurrentInputBound != OutSlotDevice.InputBound.Unbound ||
                    candidate.CurrentType.Normalize() != expectedType ||
                    output.GetDeviceType() != expectedType.ToString() ||
                    boundOutputs[inputIndex] != null)
                    return false;

                if (output is ViiperOutDevice viiper && viiper.NegotiatesHapticsConverter)
                {
                    if (!viiper.CanReuseForPhysicalController(inputIndex)) return false;
                    viiper.BindPhysicalController(inputIndex);
                }
                candidate.InputIndex = inputIndex;
                candidate.InputDisplayString = inputDisplayString ?? string.Empty;
                boundOutputs[inputIndex] = output;
                produced = output;
                // Publish only after every binding field and the produced object
                // have been committed and the manager lock has been released.
                QueueNotification(candidate.SetInputBindingDeferred(OutSlotDevice.InputBound.Bound));
            }
            DrainNotifications();
            return true;
        }

        // The caller's compatibility hint may be stale. Retire only an exact,
        // still-unbound registration, under the same lock as binding. Keep its
        // manager ownership intact if device teardown fails.
        internal bool TryRetireIncompatibleHapticsOutput(OutSlotDevice candidate,
            OutputDevice expected, int inputIndex)
        {
            if (candidate == null || expected is not ViiperOutDevice viiper) return false;
            int slot = candidate.Index;
            using (WriteLocker locker = new WriteLocker(queueLocker))
            {
                if (!IsExactAttachedSlotNoLock(candidate, expected) ||
                    candidate.CurrentInputBound != OutSlotDevice.InputBound.Unbound ||
                    viiper.CanReuseForPhysicalController(inputIndex)) return false;
                expected.RemoveFeedbacks();
                expected.Disconnect();
                outputDevices[slot] = null;
                deviceDict.Remove(slot);
                revDeviceDict.Remove(expected);
                QueueNotification(candidate.DetachDeviceDeferred(), slot, candidate);
            }
            DrainNotifications();
            return true;
        }

        internal bool IsExactBoundOutput(OutputDevice current, int inputIndex)
        {
            if (current == null || inputIndex < 0)
                return false;
            using ReadLocker locker = new ReadLocker(queueLocker);
            if (!revDeviceDict.TryGetValue(current, out int slot) ||
                (uint)slot >= outputSlots.Length)
                return false;
            OutSlotDevice candidate = outputSlots[slot];
            return IsExactAttachedSlotNoLock(candidate, current) &&
                candidate.CurrentInputBound == OutSlotDevice.InputBound.Bound &&
                candidate.InputIndex == inputIndex;
        }

        /// <summary>
        /// Withdraws one permanent input binding without retiring its output.
        /// Neutral/reset and feedback removal precede publication as reusable;
        /// failed cleanup retains the old binding instead of exposing it early.
        /// </summary>
        internal bool TryReleaseBoundOutput(OutputDevice current,
            OutputDevice[] boundOutputs, int inputIndex)
        {
            if (current == null || boundOutputs == null ||
                (uint)inputIndex >= boundOutputs.Length)
                return false;
            using (WriteLocker locker = new WriteLocker(queueLocker))
            {
                if (!revDeviceDict.TryGetValue(current, out int slot) ||
                    (uint)slot >= outputSlots.Length)
                    return false;
                OutSlotDevice candidate = outputSlots[slot];
                if (!IsExactAttachedSlotNoLock(candidate, current) ||
                    candidate.CurrentInputBound != OutSlotDevice.InputBound.Bound ||
                    candidate.InputIndex != inputIndex ||
                    !ReferenceEquals(boundOutputs[inputIndex], current))
                    return false;

                current.ResetState();
                current.RemoveFeedbacks();
                boundOutputs[inputIndex] = null;
                candidate.InputIndex = OutSlotDevice.INPUT_INDEX_DEFAULT;
                candidate.InputDisplayString = string.Empty;
                QueueNotification(candidate.SetInputBindingDeferred(OutSlotDevice.InputBound.Unbound));
            }
            DrainNotifications();
            return true;
        }

        // Retained pads stay Bound in the manager, so ordinary plug/unplug and
        // another physical controller cannot discover them as free outputs.
        internal bool TryHoldBoundOutput(OutputDevice output, OutputDevice[] bound, int index)
        {
            using WriteLocker locker = new WriteLocker(queueLocker);
            if (Volatile.Read(ref stopping) != 0) return false;
            if ((uint)index >= bound.Length || !ReferenceEquals(bound[index], output) ||
                !revDeviceDict.TryGetValue(output, out int slot)) return false;
            var candidate = outputSlots[slot];
            if (!IsExactAttachedSlotNoLock(candidate, output) || candidate.InputIndex != index ||
                candidate.CurrentInputBound != OutSlotDevice.InputBound.Bound) return false;
            output.ResetState();
            bound[index] = null;
            return true;
        }

        internal bool TryAdoptHeldOutput(OutputDevice output, OutputDevice[] bound, int index, string display)
        {
            using WriteLocker locker = new WriteLocker(queueLocker);
            if (Volatile.Read(ref stopping) != 0) return false;
            if ((uint)index >= bound.Length || bound[index] != null ||
                !revDeviceDict.TryGetValue(output, out int slot)) return false;
            var candidate = outputSlots[slot];
            if (!IsExactAttachedSlotNoLock(candidate, output) || candidate.InputIndex != index ||
                candidate.CurrentInputBound != OutSlotDevice.InputBound.Bound) return false;
            candidate.InputDisplayString = display;
            bound[index] = output;
            return true;
        }

        internal void RetireHeldOutput(OutputDevice output, OutputDevice[] bound, int index)
        {
            int slot;
            OutSlotDevice slotDevice;
            using (WriteLocker locker = new WriteLocker(queueLocker))
            {
                if ((uint)index >= bound.Length || bound[index] != null ||
                    !revDeviceDict.TryGetValue(output, out slot) ||
                    (uint)slot >= outputSlots.Length ||
                    !IsExactAttachedSlotNoLock(outputSlots[slot], output) || outputSlots[slot].InputIndex != index)
                    throw new InvalidOperationException("The retained output no longer has its exact reservation.");
                // Keep the exact manager record until Disconnect succeeds.
                // A failure remains retryable at Stop.
                output.RemoveFeedbacks();
                output.Disconnect();
                outputDevices[slot] = null;
                deviceDict.Remove(slot);
                revDeviceDict.Remove(output);
                slotDevice = outputSlots[slot];
                QueueNotification(slotDevice.DetachDeviceDeferred(), slot, slotDevice);
            }
            DrainNotifications();
        }

        // Enqueue while holding the state lock, drain after releasing it. A single
        // drainer preserves commit order across concurrent and reentrant changes.
        private void QueueNotification(Action properties, int slot = -1,
            OutSlotDevice slotDevice = null, bool assigned = false)
        {
            SlotAssignedDelegate assignedHandlers = assigned ? SlotAssigned : null;
            SlotUnassignedDelegate removedHandlers = !assigned && slot >= 0 ? SlotUnassigned : null;
            if (properties == null && assignedHandlers == null && removedHandlers == null) return;
            notifications.Enqueue(() =>
            {
                properties?.Invoke();
                if (assignedHandlers != null)
                    foreach (SlotAssignedDelegate handler in assignedHandlers.GetInvocationList())
                    {
                        try { handler(this, slot, slotDevice); }
                        catch (Exception error) { ControlService.StartupDiag($"OutputSlotManager assignment observer failed for slot #{slot + 1}: {error}"); }
                    }
                if (removedHandlers != null)
                    foreach (SlotUnassignedDelegate handler in removedHandlers.GetInvocationList())
                    {
                        try { handler(this, slot, slotDevice); }
                        catch (Exception error) { ControlService.StartupDiag($"OutputSlotManager removal observer failed for slot #{slot + 1}: {error}"); }
                    }
            });
        }

        private void DrainNotifications()
        {
            do
            {
                if (Interlocked.CompareExchange(ref drainingNotifications, 1, 0) != 0) return;
                try
                {
                    while (notifications.TryDequeue(out Action notification))
                    {
                        // Another drainer may observe an entry before its
                        // publishing writer has left the commit section.
                        using (ReadLocker barrier = new ReadLocker(queueLocker)) { }
                        notification();
                    }
                }
                finally { Volatile.Write(ref drainingNotifications, 0); }
            }
            while (!notifications.IsEmpty);
        }

        private bool IsExactAttachedSlotNoLock(OutSlotDevice candidate, OutputDevice output)
        {
            int slot = candidate.Index;
            return output != null && (uint)slot < outputSlots.Length &&
                ReferenceEquals(outputSlots[slot], candidate) &&
                candidate.CurrentAttachedStatus == OutSlotDevice.AttachedStatus.Attached &&
                ReferenceEquals(candidate.OutputDevice, output) &&
                ReferenceEquals(outputDevices[slot], output) &&
                deviceDict.TryGetValue(slot, out OutputDevice registered) &&
                ReferenceEquals(registered, output) &&
                revDeviceDict.TryGetValue(output, out int registeredSlot) &&
                registeredSlot == slot;
        }

        public void UnplugRemainingControllers(bool immediate=false)
        {
            _ = immediate;
            var failures = new List<Exception>();
            try
            {
                using (WriteLocker locker = new WriteLocker(queueLocker))
                {
                    //queuedTasks++;
                    int slotIdx = 0;
                    foreach (OutSlotDevice device in outputSlots)
                    {
                        if (device.OutputDevice != null)
                        {
                            OutputDevice output = device.OutputDevice;
                            try
                            {
                                output.Disconnect();
                                outputDevices[slotIdx] = null;
                                deviceDict.Remove(slotIdx);
                                revDeviceDict.Remove(output);
                                QueueNotification(device.DetachDeviceDeferred(), slotIdx, device);
                            }
                            catch (Exception error)
                            {
                                // Keep failed slots registered for retry, but
                                // still disconnect every other healthy output.
                                failures.Add(error);
                            }
                            //if (!immediate)
                            //{
                            //    Task.Delay(DELAY_TIME).Wait();
                            //}
                        }

                        slotIdx++;
                    }
                }
            }
            finally
            {
                // A failed slot must not prevent observers from seeing earlier
                // slots that were already disconnected and committed.
                DrainNotifications();
            }

            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Multiple virtual outputs could not be disconnected.", failures);
            //queuedTasks--;
        }
    }
}
