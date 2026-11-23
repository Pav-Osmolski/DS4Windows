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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for OutputSlotManager.xaml
    /// </summary>
    public partial class OutputSlotManagerControl : UserControl
    {
        private bool applyingColumnLayout;
        private bool layoutReady;
        private bool waitingForInitialLayout;
        private DispatcherOperation initialTableReveal;
        private readonly DispatcherTimer layoutSaveTimer = new()
        { Interval = TimeSpan.FromMilliseconds(400) };

        private void Layout_Loaded(object sender, RoutedEventArgs e)
        {
            if (!layoutReady)
            {
                double[] widths = OutputSlotsColumnLayout.Normalize(DS4Windows.Global.OutputSlotsColumnWidths);
                bool automatic = DS4Windows.Global.OutputSlotsAutoFit || widths.Length == 0;
                DS4Windows.Global.OutputSlotsAutoFit = automatic;
                DS4Windows.Global.OutputSlotsColumnWidths = widths;
                applyingColumnLayout = true;
                autoFitCheck.IsChecked = automatic;
                ApplyColumnWidths(automatic, widths);
                applyingColumnLayout = false;
                layoutReady = true;
            }

            waitingForInitialLayout = currentOutDevLV.Opacity == 0;
            ScheduleInitialTableReveal();
        }

        private void ScheduleInitialTableReveal()
        {
            if (waitingForInitialLayout && initialTableReveal == null)
                initialTableReveal = Dispatcher.InvokeAsync(RevealInitialTable, DispatcherPriority.ContextIdle);
        }

        private void RevealInitialTable()
        {
            initialTableReveal = null;
            if (autoFitCheck.IsChecked == true)
            {
                // A layout pass can finish with the input column still at its minimum.
                // Wait for it to fill the viewport, or reach its minimum in a narrow window.
                var scrollViewer = UtilMethods.FindVisualChild<ScrollViewer>(currentOutDevLV);
                if (scrollViewer == null) return;
                double viewportWidth = scrollViewer.ViewportWidth - currentOutDevLV.CellsPanelHorizontalOffset;
                if (viewportWidth <= 0) return;

                var inputColumn = currentOutDevLV.Columns[1];
                double otherColumnsWidth = currentOutDevLV.Columns.Sum(column => column.ActualWidth) - inputColumn.ActualWidth;
                double fittedWidth = Math.Clamp(viewportWidth - otherColumnsWidth, inputColumn.MinWidth, inputColumn.MaxWidth);
                if (Math.Abs(inputColumn.ActualWidth - fittedWidth) > 1) return;
            }

            waitingForInitialLayout = false;
            currentOutDevLV.Opacity = 1;
        }

        private void ApplyColumnWidths(bool automatic, double[] widths)
        {
            for (int i = 0; i < currentOutDevLV.Columns.Count; i++)
                currentOutDevLV.Columns[i].Width = automatic
                    ? (i == 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto)
                    : new DataGridLength(widths[i]);
            UpdateHorizontalScrolling();
        }

        private void UpdateHorizontalScrolling()
        {
            // A star column is resized asynchronously by DataGrid. During a
            // window shrink its previous width can briefly exceed the viewport
            // and make an Auto scrollbar appear for one layout pass. Constrain
            // the viewport while auto-fit can accommodate every column; retain
            // scrolling for genuinely narrow windows and saved manual widths.
            double requiredWidth = currentOutDevLV.Columns.Sum(column =>
                column.Width.IsStar ? column.MinWidth : Math.Max(column.MinWidth,
                    double.IsFinite(column.Width.DesiredValue) ?
                        column.Width.DesiredValue : column.ActualWidth));
            requiredWidth += currentOutDevLV.BorderThickness.Left +
                currentOutDevLV.BorderThickness.Right + SystemParameters.VerticalScrollBarWidth;
            ScrollBarVisibility visibility = autoFitCheck.IsChecked == true &&
                Math.Floor(currentOutDevLV.ActualWidth) >= Math.Ceiling(requiredWidth)
                    ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            if (currentOutDevLV.HorizontalScrollBarVisibility != visibility)
                currentOutDevLV.HorizontalScrollBarVisibility = visibility;
        }

        private double[] CaptureColumnWidths() => currentOutDevLV.Columns.Select(column =>
            Math.Max(column.MinWidth, column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth)).ToArray();

        private void ColumnResize_Completed(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (applyingColumnLayout || !layoutReady || e.Canceled || Math.Abs(e.HorizontalChange) < 0.1) return;
            DependencyObject target = e.OriginalSource as DependencyObject;
            while (target != null && target is not System.Windows.Controls.Primitives.DataGridColumnHeader)
                target = VisualTreeHelper.GetParent(target);
            if (target is not System.Windows.Controls.Primitives.DataGridColumnHeader header || header.Column == null) return;
            currentOutDevLV.UpdateLayout();
            // Capture the completed user gesture, including a formerly flexible star column.
            double[] widths = CaptureColumnWidths();
            if (!DS4Windows.Global.OutputSlotsAutoFit &&
                widths.SequenceEqual(DS4Windows.Global.OutputSlotsColumnWidths ?? Array.Empty<double>())) return;
            applyingColumnLayout = true;
            autoFitCheck.IsChecked = false;
            ApplyColumnWidths(false, widths);
            applyingColumnLayout = false;
            DS4Windows.Global.OutputSlotsAutoFit = false;
            DS4Windows.Global.OutputSlotsColumnWidths = widths;
            QueueLayoutSave();
        }

        private void AutoFit_Changed(object sender, RoutedEventArgs e)
        {
            if (applyingColumnLayout || !layoutReady) return;
            bool automatic = autoFitCheck.IsChecked == true;
            double[] widths = OutputSlotsColumnLayout.Normalize(DS4Windows.Global.OutputSlotsColumnWidths);
            if (widths.Length == 0) widths = CaptureColumnWidths();
            applyingColumnLayout = true;
            ApplyColumnWidths(automatic, widths);
            applyingColumnLayout = false;
            DS4Windows.Global.OutputSlotsAutoFit = automatic;
            if (!automatic) DS4Windows.Global.OutputSlotsColumnWidths = widths;
            QueueLayoutSave();
        }

        private void FitColumns_Click(object sender, RoutedEventArgs e)
        {
            autoFitCheck.IsChecked = true;
            applyingColumnLayout = true;
            ApplyColumnWidths(true, Array.Empty<double>());
            applyingColumnLayout = false;
            DS4Windows.Global.OutputSlotsAutoFit = true;
            QueueLayoutSave();
        }

        private void QueueLayoutSave()
        {
            layoutSaveTimer.Stop();
            layoutSaveTimer.Start();
        }

        private void LayoutSaveTimer_Tick(object sender, EventArgs e)
        {
            layoutSaveTimer.Stop();
            DS4Windows.Global.Save();
        }

        private void Layout_Unloaded(object sender, RoutedEventArgs e)
        {
            waitingForInitialLayout = false;
            initialTableReveal?.Abort();
            initialTableReveal = null;
            if (layoutSaveTimer.IsEnabled) LayoutSaveTimer_Tick(this, EventArgs.Empty);
        }

        private DS4Windows.ControlService controlService;
        private CurrentOutDeviceViewModel currentOutDevVM;
        //private PermanentOutDevViewModel permanentDevVM;

        public OutputSlotManagerControl()
        {
            InitializeComponent();
            layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
            currentOutDevLV.SizeChanged += (_, _) => UpdateHorizontalScrolling();
            currentOutDevLV.LayoutUpdated += (_, _) =>
            {
                UpdateHorizontalScrolling();
                ScheduleInitialTableReveal();
            };
            currentOutDevLV.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler(ColumnResize_Completed), true);
        }

        public void SetupDataContext(DS4Windows.ControlService controlService,
            DS4Windows.OutputSlotManager outputMan)
        {
            this.controlService = controlService;

            currentOutDevVM = new CurrentOutDeviceViewModel(controlService, outputMan);
            currentOutDevVM.SelectedIndexChanged += CurrentOutDevVM_SelectedIndexChanged;
            currentOutDevLV.DataContext = currentOutDevVM;
            sideStackPanel.DataContext = currentOutDevVM;
            plugDevStackPanel.DataContext = currentOutDevVM;
            outSlotStackPanel.DataContext = null;

            //permanentDevVM = new PermanentOutDevViewModel(controlService, outputMan);
            //permanentOutDevLV.DataContext = permanentDevVM;
        }

        private void CurrentOutDevVM_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = currentOutDevVM.SelectedIndex;
            if (idx >= 0)
            {
                outSlotStackPanel.DataContext = currentOutDevVM.SlotDeviceEntries[idx];
            }
            else
            {
                outSlotStackPanel.DataContext = null;
            }
        }

        public void SetupLateEvents()
        {

        }

        private void PluginBtn_Click(object sender, RoutedEventArgs e)
        {
            int idx = currentOutDevVM.SelectedIndex;
            SlotDeviceEntry tempEntry = null;
            if (idx >= 0)
            {
                tempEntry = currentOutDevVM.SlotDeviceEntries[idx];
            }

            if (tempEntry != null &&
                tempEntry.OutSlotDevice.CurrentReserveStatus ==
                DS4Control.OutSlotDevice.ReserveStatus.Permanent &&
                tempEntry.OutSlotDevice.PermanentType != DS4Windows.OutContType.None)
            {
                tempEntry.OutSlotDevice.CurrentType = tempEntry.OutSlotDevice.PermanentType;
                tempEntry.RequestPlugin();
            }
            else
            {
                PluginOutDevWindow devWindow = new PluginOutDevWindow();
                devWindow.ShowDialog();
                MessageBoxResult result = devWindow.Result;
                if (result == MessageBoxResult.OK)
                {
                    tempEntry.OutSlotDevice.CurrentType = devWindow.ContType;
                    tempEntry.OutSlotDevice.CurrentReserveStatus = devWindow.ReserveType;
                    if (tempEntry.OutSlotDevice.CurrentReserveStatus ==
                        DS4Control.OutSlotDevice.ReserveStatus.Permanent)
                    {
                        tempEntry.OutSlotDevice.PermanentType = devWindow.ContType;
                    }

                    tempEntry.RequestPlugin();
                }
            }
        }

        private void UnplugBtn_Click(object sender, RoutedEventArgs e)
        {
            int idx = currentOutDevVM.SelectedIndex;
            if (idx >= 0)
            {
                currentOutDevVM.SlotDeviceEntries[idx].RequestUnplug();
            }
        }

        private void SlotChangeAcceptBtn_Click(object sender, RoutedEventArgs e)
        {
            int idx = currentOutDevVM.SelectedIndex;
            if (idx >= 0)
            {
                currentOutDevVM.SlotDeviceEntries[idx].ApplyChanges();
            }
        }
    }
}
