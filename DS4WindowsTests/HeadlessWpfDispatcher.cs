using System.Windows.Threading;

namespace DS4WindowsTests;

/// <summary>
/// A process-lifetime dispatcher for localized WPF controls. Localization retains
/// dispatcher ownership between tests, so a temporary STA thread is insufficient.
/// Callers that share application globals must remain nonparallel test classes.
/// </summary>
internal static class HeadlessWpfDispatcher
{
    private static readonly Lazy<Dispatcher> uiDispatcher = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true, Name = "Headless WPF test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    internal static void Invoke(Action action, Func<string> progress = null)
    {
        // Allow nested fixture helpers without dispatching work behind ourselves.
        if (uiDispatcher.Value.CheckAccess())
        {
            action();
            return;
        }
        var operation = uiDispatcher.Value.InvokeAsync(action);
        Assert.AreSame(operation.Task,
            Task.WhenAny(operation.Task, Task.Delay(TimeSpan.FromSeconds(30))).GetAwaiter().GetResult(),
            $"Headless WPF test timed out: {progress?.Invoke() ?? "render / selection"}.");
        operation.Task.GetAwaiter().GetResult();
    }
}
