using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DS4WinWPF.DS4Forms;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public class DeferredProfilePageTests
{
    [TestMethod]
    public void UnvisitedPagesDoNotConstructControlsAndVisitedPagesRetainEdits()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var page = Page();
            int constructions = 0;
            page.Materializing += (_, _) => constructions++;
            page.Materializing += (_, _) => page.EnsureContent();
            page.EnsureContent();
            Assert.IsNull(page.Content, "Profile model must be ready before any page can bind.");
            page.Ready = true;
            Assert.IsNull(page.Content, "Readiness alone must not construct a hidden page.");
            page.EnsureContent();
            var text = (TextBox)page.Content;
            text.Text = "Unsaved edit";
            page.Visibility = Visibility.Collapsed;
            page.Visibility = Visibility.Visible;
            page.EnsureContent();
            Assert.AreSame(text, page.Content);
            Assert.AreEqual("Unsaved edit", text.Text);
            Assert.AreEqual(1, constructions);
        });
    }

    [TestMethod]
    public void RetiredEditorCannotConstructAPageFromALateCallback()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var page = Page();
            page.Ready = true;
            page.Retired = true;
            page.EnsureContent();
            Assert.IsFalse(page.IsMaterialized);
        });
    }

    [TestMethod]
    public void SeparateEditingSessionsNeverShareEditableControls()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var first = Page();
            var second = Page();
            second.PageTemplate = first.PageTemplate;
            first.Ready = second.Ready = true;
            first.EnsureContent(); second.EnsureContent();
            ((TextBox)first.Content).Text = "First profile";
            Assert.AreNotSame(first.Content, second.Content);
            Assert.AreEqual("", ((TextBox)second.Content).Text);
        });
    }

    [TestMethod]
    public void CreatingAPageReadsExistingModelWithoutReplacingItWithControlDefaults()
    {
        HeadlessWpfDispatcher.Invoke(() =>
        {
            var model = new TextModel { Text = "Preserved profile value" };
            var factory = new FrameworkElementFactory(typeof(TextBox));
            factory.SetBinding(TextBox.TextProperty, new Binding("Text")
                { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var page = new DeferredProfilePage
                { PageTemplate = new DataTemplate { VisualTree = factory }, DataContext = model, Ready = true };
            page.EnsureContent();
            page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            ((TextBox)page.Content).GetBindingExpression(TextBox.TextProperty).UpdateTarget();
            Assert.AreEqual("Preserved profile value", ((TextBox)page.Content).Text);
            Assert.AreEqual("Preserved profile value", model.Text);
        });
    }

    private static DeferredProfilePage Page() => new()
        { PageTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBox)) } };

    public sealed class TextModel { public string Text { get; set; } }
}
