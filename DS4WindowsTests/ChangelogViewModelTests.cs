using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass]
public sealed class ChangelogViewModelTests
{
    [TestMethod]
    public async Task FailedOrEmptyFetchCanBeRetriedSuccessfully()
    {
        int attempt = 0;
        var model = new ChangelogViewModel(() => ++attempt switch
        {
            1 => Task.FromException<string>(new System.Net.Http.HttpRequestException("offline")),
            2 => Task.FromResult(""),
            _ => Task.FromResult("## Version 5.0.33\n\nRelease notes."),
        });
        await model.DisplayChangelog();
        Assert.IsTrue(model.HasError && model.ShowStatus);
        Assert.IsFalse(model.IsLoading);
        await model.DisplayChangelog();
        Assert.IsTrue(model.HasError);
        await model.DisplayChangelog();
        Assert.IsFalse(model.HasError || model.ShowStatus || model.IsLoading);
        StringAssert.Contains(model.Markdown, "5.0.33");
    }

    [TestMethod]
    public async Task ConcurrentLoadsShareOneFetchAndNotifyTheVisibleStatus()
    {
        int calls = 0;
        var response = new TaskCompletionSource<string>();
        var model = new ChangelogViewModel(() => { calls++; return response.Task; });
        var notifications = new List<string>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var first = model.DisplayChangelog();
        Assert.IsTrue(model.IsLoading && model.ShowStatus);
        Assert.IsFalse(model.HasError);
        await model.DisplayChangelog();
        Assert.AreEqual(1, calls);
        response.SetResult("## Loaded");
        await first;
        Assert.AreEqual("## Loaded", model.Markdown);
        CollectionAssert.Contains(notifications, nameof(model.Markdown));
        CollectionAssert.Contains(notifications, nameof(model.ShowStatus));
        Assert.IsFalse(model.ShowStatus);
    }
}
