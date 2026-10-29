using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class ProfileSwitchFeedbackTests
{
    [TestMethod]
    [DataRow(true, true, 1, 1)]
    [DataRow(true, false, 1, 0)]
    [DataRow(false, true, 0, 0)]
    public void OnlyAppliedSelectionsAnnounceAndRespectNotificationPreference(
        bool applied, bool enabled, int expectedLog, int expectedTray)
    {
        bool previous = Global.ProfileChangedNotification;
        int logs = 0, notifications = 0;
        EventHandler<DebugEventArgs> gui = (_, e) =>
        {
            StringAssert.Contains(e.Data, "Test Blue");
            logs++;
        };
        EventHandler<DebugEventArgs> tray = (_, e) =>
        {
            StringAssert.Contains(e.Data, "Test Blue");
            notifications++;
        };
        AppLogger.GuiLog += gui;
        AppLogger.TrayIconLog += tray;
        try
        {
            Global.ProfileChangedNotification = enabled;
            ProfileSwitchFeedback.ReportApplied(applied, 0, "Test Blue", 87);
            Assert.AreEqual(expectedLog, logs);
            Assert.AreEqual(expectedTray, notifications);
        }
        finally
        {
            AppLogger.GuiLog -= gui;
            AppLogger.TrayIconLog -= tray;
            Global.ProfileChangedNotification = previous;
        }
    }
}
