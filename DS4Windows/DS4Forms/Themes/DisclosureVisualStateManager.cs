using System.Windows;

namespace DS4WinWPF.DS4Forms.Themes;

// Keep transitions local to the indicator; content opens immediately. Honour
// the Windows animation preference without retaining animated theme brushes.
public sealed class DisclosureVisualStateManager : VisualStateManager
{
    public static Duration HoverDuration => MotionDuration(140);
    public static Duration PressDuration => MotionDuration(80);
    public static Duration ReleaseDuration => MotionDuration(120);
    private static Duration MotionDuration(int milliseconds) => new(System.TimeSpan.FromMilliseconds(
        SystemParameters.ClientAreaAnimation ? milliseconds : 0));
    protected override bool GoToStateCore(FrameworkElement control, FrameworkElement stateGroupsRoot,
        string stateName, VisualStateGroup group, VisualState state, bool useTransitions)
    {
        // ButtonBase also requests optional focus/validation states which this
        // template delegates to the normal focus adorner.
        return state != null && group != null && base.GoToStateCore(control, stateGroupsRoot,
            stateName, group, state, useTransitions && SystemParameters.ClientAreaAnimation);
    }
}
