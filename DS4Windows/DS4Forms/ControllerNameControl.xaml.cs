using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WinWPF.DS4Forms;

public partial class ControllerNameControl : UserControl
{
    private CompositeDeviceModel editingController;
    private bool contextMenuOpen;
    private string initialText;
    private string editingIdentity;
    private Window owner;
    internal bool IsEditing => editingController != null;

    public ControllerNameControl()
    {
        InitializeComponent();
        nameEditor.MaxLength = ControllerNameStore.MaximumLength;
        renameButton.ToolTip = UiText.Get("Rename controller");
        AutomationProperties.SetName(renameButton, UiText.Get("Rename controller"));
        AutomationProperties.SetName(nameEditor, UiText.Get("Controller name"));
        nameEditor.ToolTip = UiText.Get("Use up to 50 letters, numbers and spaces.") + "\n" + UiText.Get("Clear the name to restore the default. Enter saves; Escape cancels.");
        DataObject.AddPastingHandler(nameEditor, Editor_Pasting);
        LostKeyboardFocus += (_, _) => QueueFocusCommit();
        nameEditor.ContextMenuOpening += (_, _) => contextMenuOpen = true;
        nameEditor.ContextMenuClosing += (_, _) => { contextMenuOpen = false; QueueFocusCommit(); };
        DataContextChanged += (_, _) => { if (IsEditing) CommitEdit(); CancelEdit(); };
        Loaded += (_, _) => HookWindow();
        Unloaded += (_, _) => { if (IsEditing) CommitEdit(); CancelEdit(); UnhookWindow(); };
        nameDisplay.ToolTipOpening += (_, _) => (DataContext as CompositeDeviceModel)?.RequestUpdatedTooltipID();
    }

    private void HookWindow()
    {
        Window current = Window.GetWindow(this);
        if (owner == current) return;
        UnhookWindow();
        owner = current;
        if (owner == null) return;
        owner.PreviewMouseDown += Owner_PreviewMouseDown;
        owner.Deactivated += Owner_Deactivated;
    }

    private void UnhookWindow()
    {
        if (owner == null) return;
        owner.PreviewMouseDown -= Owner_PreviewMouseDown;
        owner.Deactivated -= Owner_Deactivated;
        owner = null;
    }

    private void Owner_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsEditing && !contextMenuOpen && e.OriginalSource is DependencyObject source &&
            !(ReferenceEquals(source, nameEditor) || source is System.Windows.Media.Visual && nameEditor.IsAncestorOf(source))) CommitEdit();
    }

    private void Owner_Deactivated(object sender, EventArgs e)
    {
        if (IsEditing && !contextMenuOpen) CommitEdit();
    }

    private void Rename_Click(object sender, RoutedEventArgs e) { BeginEdit(); e.Handled = true; }

    internal void BeginEdit()
    {
        if (DataContext is not CompositeDeviceModel controller || !controller.CanRenameController) return;
        editingController = controller;
        editingIdentity = controller.Device.MacAddress;
        nameEditor.Text = string.IsNullOrEmpty(controller.CustomName) ? controller.DefaultControllerName : controller.CustomName;
        initialText = nameEditor.Text;
        ShowError(null);
        nameDisplay.Visibility = Visibility.Collapsed;
        editorPanel.Visibility = Visibility.Visible;
        nameEditor.Focus();
        nameEditor.SelectAll();
    }

    internal bool CommitEdit()
    {
        if (editingController == null) return true;
        if (nameEditor.Text == initialText || editingController.Device.MacAddress != editingIdentity)
        { CancelEdit(); return true; }
        if (!editingController.TryRenameController(nameEditor.Text, out string error))
        {
            ShowError(error);
            return false;
        }
        CancelEdit();
        return true;
    }

    internal void CancelEdit()
    {
        editingController = null;
        nameDisplay.Visibility = Visibility.Visible;
        editorPanel.Visibility = Visibility.Collapsed;
        ShowError(null);
    }

    private void QueueFocusCommit() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
    {
        if (IsEditing && !IsKeyboardFocusWithin && !contextMenuOpen) CommitEdit();
    }));

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (CommitEdit()) renameButton.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelEdit();
            renameButton.Focus();
        }
    }

    private bool CanInsert(string text)
    {
        string proposed = nameEditor.Text.Remove(nameEditor.SelectionStart, nameEditor.SelectionLength)
            .Insert(nameEditor.SelectionStart, text);
        if (ControllerNameStore.TryNormalize(proposed, out _)) return true;
        ShowError(UiText.Get("Use up to 50 letters, numbers and spaces."));
        return false;
    }

    private void Editor_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !CanInsert(e.Text);

    private void Editor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText) ||
            !CanInsert(e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty)) e.CancelCommand();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) { if (renameError != null) ShowError(null); }

    private void ShowError(string message)
    {
        renameError.Text = message ?? string.Empty;
        renameError.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
