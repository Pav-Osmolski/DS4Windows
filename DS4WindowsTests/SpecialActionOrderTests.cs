using DS4Windows;
using DS4WinWPF.DS4Forms.ViewModels;

namespace DS4WindowsTests;

[TestClass, DoNotParallelize]
public class SpecialActionOrderTests
{
    [TestMethod]
    public async Task MoveAndSortPersistDisplayOrderKeepSelectionAndPreserveEnabledExecutionOrder()
    {
        string root = Directory.CreateTempSubdirectory("ds4w-action-order-").FullName;
        var storage = new SettingsStorage(root + "/program", root + "/appdata", root + "/preferences");
        var original = Global.ProfileActions[0];
        var actions = new[] { "Zulu", "Alpha / Pause", "Bravo" }.Select(name => new SpecialAction(name, "Cross", "Macro", "65/65")).ToArray();
        var vm = new SpecialActionsListViewModel(0) { Storage = () => storage };
        try
        {
            Global.ProfileActions[0] = new List<string> { "Zulu", "Bravo" };
            vm.LoadActions(actions);
            var selected = vm.ActionCol[2];
            vm.CurrentSpecialActionItem = selected;
            vm.SpecialActionIndex = 2;
            Assert.IsFalse(vm.CanMoveDown);
            Assert.IsTrue(vm.CanMoveUp);
            vm.MoveSelectedAction(-1);
            Assert.AreSame(selected, vm.ActionCol[1]);
            vm.SortActions(nameof(SpecialActionItem.ActionName), false);
            CollectionAssert.AreEqual(new[] { "Alpha / Pause", "Bravo", "Zulu" }, vm.ActionCol.Select(a => a.ActionName).ToArray());
            Assert.AreSame(selected, vm.CurrentSpecialActionItem);
            Assert.AreEqual(1, vm.SpecialActionIndex);
            vm.ExportEnabledActions();
            CollectionAssert.AreEqual(new[] { "Zulu", "Bravo" }, Global.ProfileActions[0], "Visual ordering must not change execution priority.");
            await vm.OrderSaved;
            var reopened = new SpecialActionsListViewModel(0) { Storage = () => storage };
            reopened.LoadActions(actions.Append(new SpecialAction("New action", "Circle", "Macro", "66/66")));
            CollectionAssert.AreEqual(new[] { "Alpha / Pause", "Bravo", "Zulu", "New action" }, reopened.ActionCol.Select(a => a.ActionName).ToArray());
            for (int i = 0; i < reopened.ActionCol.Count; i++) Assert.AreEqual(i, reopened.ActionCol[i].Index);
            reopened.ActionCol.RemoveAt(1);
            for (int i = 0; i < reopened.ActionCol.Count; i++) Assert.AreEqual(i, reopened.ActionCol[i].Index);
        }
        finally { await vm.OrderSaved; Global.ProfileActions[0] = original; Global.CacheExtraProfileInfo(0); Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task BoundaryMovesAreNoOpsAndUnreadablePreferencesDoNotBreakActions()
    {
        var original = Global.ProfileActions[0];
        var vm = new SpecialActionsListViewModel(0) { Storage = () => throw new IOException("read only") };
        try
        {
            Global.ProfileActions[0] = new();
            vm.LoadActions(new[] { new SpecialAction("Only", "Cross", "Macro", "65/65") });
            vm.CurrentSpecialActionItem = vm.ActionCol[0];
            vm.SpecialActionIndex = 0;
            Assert.IsFalse(vm.CanMoveUp); Assert.IsFalse(vm.CanMoveDown);
            vm.MoveSelectedAction(-1); vm.MoveSelectedAction(1); vm.MoveSelectedAction(0);
            Assert.IsTrue(vm.OrderSaved.IsCompleted);
            vm.SortActions(nameof(SpecialActionItem.ActionName), true);
            await vm.OrderSaved;
            Assert.AreEqual("Only", vm.ActionCol[0].ActionName);
            Assert.IsFalse(vm.ActionCol[0].Active);
        }
        finally { await vm.OrderSaved; Global.ProfileActions[0] = original; }
    }
}
