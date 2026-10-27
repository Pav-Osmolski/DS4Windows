using System.Windows;

namespace DS4WinWPF.DS4Forms.Themes;

// Keep transitions local to the indicator; content opens immediately. Honour
// the Windows animation preference without retaining animated theme brushes.
public sealed class DisclosureVisualStateManager : VisualStateManager
{
    protected override bool GoToStateCore(FrameworkElement control, FrameworkElement stateGroupsRoot,
        string stateName, VisualStateGroup group, VisualState state, bool useTransitions)
    {
        // ButtonBase also requests optional focus/validation states which this
        // template delegates to the normal focus adorner.
        return state != null && group != null && base.GoToStateCore(control, stateGroupsRoot,
            stateName, group, state, useTransitions && SystemParameters.ClientAreaAnimation);
    }
}
