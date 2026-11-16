using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Mouse = System.Windows.Input.Mouse;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ControllerNameTests
{
    private const string Mac = "BC:C7:46:60:57:DE";
    private string root;
    private string FilePath => Path.Combine(root, "ControllerNames.json");
    private ControllerNameStore Store => new(FilePath);
    [TestInitialize] public void Initialize() => root = Directory.CreateTempSubdirectory("ds4w-controller-names-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    private sealed class Device : DS4Device
    {
        internal Device(string mac = ControllerNameTests.Mac, string designation = "DualSense", ConnectionType connection = ConnectionType.USB)
            : base(designation, InputDeviceType.DualSense, connection) { this.Mac = mac; }
        internal void ChangeMac(string value) => this.Mac = value;
    }

    private CompositeDeviceModel Model(ControllerNameStore store = null, string mac = Mac, ConnectionType connection = ConnectionType.USB)
        => new(new Device(mac, connection: connection), Global.TEST_PROFILE_INDEX, null, null, store ?? Store);

    [TestMethod]
    public void NamesSurviveReconnectAndTransportChangesWithoutDependingOnProfiles()
    {
        var first = Model();
        Assert.IsTrue(first.TryRenameController("  Blue Controller 2  ", out _));
        var reconnect = Model(connection: ConnectionType.BT);
        Assert.AreEqual("Blue Controller 2", reconnect.ControllerDisplayName);
        Assert.AreEqual("Blue Controller 2 (" + Mac + ")", reconnect.IdText);
        Assert.AreEqual("DualSense", Model(mac: "10:20:30:40:50:60").ControllerDisplayName);
        Assert.AreEqual("Blue Controller 2", new ControllerNameStore(FilePath).Get("bcc7466057de"));
        Assert.IsFalse(Directory.EnumerateFiles(root, "*.xml").Any());
        Assert.IsTrue(reconnect.TryRenameController("   ", out _));
        Assert.AreEqual("DualSense", reconnect.ControllerDisplayName);
        Assert.AreEqual("DualSense", Model().ControllerDisplayName);
    }

    [TestMethod]
    [DataRow("Blue-Controller")]
    [DataRow("../Red")]
    [DataRow("Blue\tController")]
    [DataRow("Blue\nController")]
    [DataRow("Blue\u200BController")]
    [DataRow("Blue 🎮")]
    public void InvalidNamesNeverReplaceThePreviousSavedName(string invalid)
    {
        Store.Save(Mac, "Blue");
        byte[] original = File.ReadAllBytes(FilePath);
        Assert.IsFalse(Model().TryRenameController(invalid, out string error));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(FilePath));
    }

    [TestMethod]
    public void LengthAndUnicodeLettersAreValidatedWithoutAllowingControls()
    {
        Assert.AreEqual(new string('A', 50), Store.Save(Mac, new string('A', 50)));
        Assert.ThrowsExactly<ArgumentException>(() => Store.Save(Mac, new string('A', 51)));
        Assert.AreEqual("Żółty 手柄 2", Store.Save(Mac, "Żółty 手柄 2"));
        Assert.AreEqual("Café", Store.Save(Mac, "Cafe\u0301"));
        Assert.IsFalse(ControllerNameStore.TryNormalize("\uD800", out _));
        Assert.IsFalse(ControllerNameStore.TryKey("00:00:00:00:00:00", out _));
        Assert.IsFalse(ControllerNameStore.TryKey("not a MAC", out _));
        Assert.IsFalse(ControllerNameStore.TryKey("FF:FF:FF:FF:FF:FF", out _));
    }

    [TestMethod]
    public void FailedAtomicPublicationKeepsEveryPreviouslySavedNameAndTheVisibleName()
    {
        var store = Store;
        store.Save(Mac, "Blue");
        store.Save("10:20:30:40:50:60", "Red");
        byte[] original = File.ReadAllBytes(FilePath);
        var model = Model(store);
        store.BeforePublishForTest = () => throw new IOException("Injected write failure");
        Assert.IsFalse(model.TryRenameController("Green", out _));
        Assert.AreEqual("Blue", model.ControllerDisplayName);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(FilePath));
        Assert.IsFalse(Directory.EnumerateFiles(root, "*.tmp").Any());
    }

    [TestMethod]
    [DataRow("{not JSON")]
    [DataRow("{\"Version\":2,\"Names\":{}}")]
    [DataRow("{\"Version\":1,\"Names\":{\"BCC7466057DE\":\"Unsafe\\nName\"}}")]
    public void CorruptOrFutureDocumentsAreNotSilentlyOverwritten(string text)
    {
        File.WriteAllText(FilePath, text);
        var model = Model();
        Assert.AreEqual("DualSense", model.ControllerDisplayName);
        Assert.IsFalse(model.TryRenameController("Blue", out _));
        Assert.AreEqual(text, File.ReadAllText(FilePath));
    }

    [TestMethod]
    public void NameBindingsAndMacIdentityRefreshTogether()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var model = Model();
            var title = new TextBlock();
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.ControllerDisplayName)) { Source = model });
            var identity = new TextBlock();
            identity.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.IdText)) { Source = model });
            Assert.IsTrue(model.TryRenameController("Blue", out _));
            Assert.AreEqual("Blue", title.Text);
            Assert.AreEqual("Blue (" + Mac + ")", identity.Text);
            ((Device)model.Device).ChangeMac("10:20:30:40:50:60");
            model.RequestUpdatedIdentity();
            Assert.AreEqual("DualSense", title.Text);
            StringAssert.Contains(identity.Text, "10:20:30:40:50:60");
        });
    }

    [TestMethod]
    public void EnterCommitsEscapeCancelsAndFailureKeepsTheDraftAvailable()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var store = Store;
            var model = Model(store);
            var control = new ControllerNameControl { DataContext = model };
            var editor = (TextBox)control.FindName("nameEditor");
            control.BeginEdit(); editor.Text = "Blue";
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.IsFalse(control.IsEditing);
            Assert.AreEqual("Blue", Store.Get(Mac));
            control.BeginEdit(); editor.Text = "Red";
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.AreEqual("Blue", model.ControllerDisplayName);
            control.BeginEdit(); editor.Text = "Green";
            store.BeforePublishForTest = () => throw new IOException("Injected failure");
            Assert.IsFalse(control.CommitEdit());
            Assert.IsTrue(control.IsEditing);
            Assert.AreEqual("Green", editor.Text);
            Assert.AreEqual("Blue", model.ControllerDisplayName);
            control.CancelEdit();
        });
    }

    [TestMethod]
    public void LeavingTheEditorSavesAndChangingRowsCannotRenameTheWrongController()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var first = Model();
            var second = Model(mac: "10:20:30:40:50:60");
            var control = new ControllerNameControl { DataContext = first };
            var editor = (TextBox)control.FindName("nameEditor");
            control.BeginEdit(); editor.Text = "Blue";
            control.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, editor, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
            Assert.AreEqual("Blue", Store.Get(Mac));
            Assert.IsFalse(control.IsEditing);
            control.BeginEdit(); editor.Text = "Green";
            control.DataContext = second;
            Assert.AreEqual("Green", Store.Get(Mac));
            Assert.AreEqual(string.Empty, Store.Get(second.Device.MacAddress));
            Assert.IsFalse(control.IsEditing);
        });
    }

    [TestMethod]
    public void PasteValidationUsesTheEntireReplacementAndDoesNotChangeTheClipboard()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var control = new ControllerNameControl { DataContext = Model() };
            var editor = (TextBox)control.FindName("nameEditor");
            control.BeginEdit();
            foreach (string text in new[] { "Red\nController", "Red/Controller", new string('A', 51), "Red Controller" })
            {
                editor.SelectAll();
                var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText);
                editor.RaiseEvent(paste);
                Assert.AreEqual(text != "Red Controller", paste.CommandCancelled);
            }
            control.CancelEdit();
        });
    }

    [TestMethod]
    public void AChangedPhysicalIdentityCannotReceiveThePreviousControllersDraft()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var model = Model();
            var control = new ControllerNameControl { DataContext = model };
            control.BeginEdit();
            ((TextBox)control.FindName("nameEditor")).Text = "Blue";
            ((Device)model.Device).ChangeMac("10:20:30:40:50:60");
            model.RequestUpdatedIdentity();
            Assert.IsTrue(control.CommitEdit());
            Assert.AreEqual(string.Empty, Store.Get(Mac));
            Assert.AreEqual(string.Empty, Store.Get(model.Device.MacAddress));
        });
    }

    [TestMethod]
    public void NonFocusableOutsideClicksAndWindowDeactivationSaveWithoutDependingOnFocus()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var model = Model();
            var control = new ControllerNameControl { DataContext = model };
            var app = Application.Current;
            var oldMode = app?.ShutdownMode;
            var oldMain = app?.MainWindow;
            if (app != null) app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new TestWindow { Content = control };
            try
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                control.BeginEdit(); ((TextBox)control.FindName("nameEditor")).Text = "Blue";
                window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.AreEqual("Blue", Store.Get(Mac));
                Assert.IsFalse(control.IsEditing);
                control.BeginEdit(); ((TextBox)control.FindName("nameEditor")).Text = "Green";
                window.DeactivateForTest();
                Assert.AreEqual("Green", Store.Get(Mac));
                Assert.IsFalse(control.IsEditing);
            }
            finally
            {
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); window.Close();
                if (app != null) { app.MainWindow = oldMain; app.ShutdownMode = oldMode.Value; }
            }
        });
    }

    private sealed class TestWindow : Window
    {
        internal void DeactivateForTest() => OnDeactivated(EventArgs.Empty);
    }

    [TestMethod]
    public void BackupAndSettingsLocationMigrationCarryTheSeparateNamesDocument()
    {
        string program = Path.Combine(root, "Program"), appData = Path.Combine(root, "AppData");
        Directory.CreateDirectory(program); Directory.CreateDirectory(appData);
        File.WriteAllText(Path.Combine(program, "Profiles.xml"), "<DS4Windows />");
        new ControllerNameStore(Path.Combine(program, "ControllerNames.json")).Save(Mac, "Blue");
        var storage = new SettingsStorage(program, appData, Path.Combine(root, "Preferences"));
        storage.Remember(SettingsFolder.Program);
        string archive = Path.Combine(root, "backup.zip");
        storage.BackUp(program, archive);
        storage.Queue(SettingsFolder.Program, SettingsFolder.AppData, true, false);
        storage.ApplyPending();
        Assert.AreEqual("Blue", new ControllerNameStore(Path.Combine(appData, "ControllerNames.json")).Get(Mac));
        storage.QueueRestore(archive, SettingsFolder.Program); storage.ApplyPending();
        Assert.AreEqual("Blue", new ControllerNameStore(Path.Combine(program, "ControllerNames.json")).Get(Mac));
    }

    [TestMethod]
    public void RenameControlWrapsAndRendersWithThemeColorsAtNormalAndNarrowWidths()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            foreach (var theme in Enum.GetValues<AppThemeChoice>())
            foreach (int width in new[] { 520, 300 })
            {
                var model = Model();
                Assert.IsTrue(model.TryRenameController("Blue DualSense Blue DualSense Blue DualSense Blue", out _));
                var control = new ControllerNameControl { DataContext = model, Width = width, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
                var palette = AppThemeCatalog.Create(theme, highContrast: false);
                var surface = new Border { Child = control, Width = width + 24, Height = 180 };
                surface.Resources.MergedDictionaries.Add(palette);
                surface.SetResourceReference(Border.BackgroundProperty, "BackgroundColor");
                surface.Measure(new Size(width + 24, 300)); surface.Arrange(new Rect(0, 0, width + 24, 180)); surface.UpdateLayout();
                var display = (TextBlock)control.FindName("nameDisplay");
                Assert.AreEqual(TextWrapping.Wrap, display.TextWrapping);
                Assert.IsTrue(display.ActualHeight > 22, "The long name plus MAC must wrap.");
                Assert.IsTrue(control.ActualWidth <= width);
                Assert.AreEqual(((SolidColorBrush)palette["ForegroundColor"]).Color, ((SolidColorBrush)display.Foreground).Color);
                var identity = (TextBlock)control.FindName("displayIdentity");
                StringAssert.Contains(identity.Text, Mac);
                string output = Environment.GetEnvironmentVariable("DS4W_CONTROLLER_NAME_ARTIFACTS");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"name-{theme}-{width}.png")); encoder.Save(stream);
                }
            }
        });
    }

    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null;
    }
}
