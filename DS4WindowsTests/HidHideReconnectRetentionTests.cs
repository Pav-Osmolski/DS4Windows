using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using System.Xml.Serialization;

namespace DS4WindowsTests;

[TestClass]
public sealed class HidHideReconnectRetentionTests
{
    private const string Owned = @"HID\VID_054C&PID_0CE6\OWNED";
    private const string User = @"HID\VID_054C&PID_0CE6\USER";

    [TestMethod]
    public void EnabledRetentionKeepsOnlyOwnedEntriesUntilServiceStop()
    {
        HidHideManagedDeviceRegistry<object> registry = new();
        registry.OpenLifecycle(keepHiddenOnReconnect: true);
        object source = new();
        var claim = registry.BeginConnection(source, new[] { Owned, User });
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.GetUncoveredIds(claim, new[] { User }).ToArray());
        registry.CompleteConnection(claim, Array.Empty<string>(), new[] { Owned });
        Assert.AreEqual(0, registry.Disconnect(source).PersistentReleaseIds.Count);
        Assert.AreEqual(0, registry.RevalidatePersistentRelease(new[] { Owned, User }).Count);
        Assert.IsFalse(registry.HasConnections);
        Assert.IsTrue(registry.HasOwnedIds);

        var replacement = registry.BeginConnection(new object(), new[] { Owned, User });
        Assert.AreEqual(0, registry.GetUncoveredIds(replacement, new[] { Owned, User }).Count);
        var stop = registry.BeginServiceRelease();
        CollectionAssert.AreEquivalent(new[] { Owned }, stop.PersistentIds.ToArray());
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.RevalidatePersistentRelease(stop.PersistentIds).ToArray());
        Assert.AreEqual(0, registry.CompletePersistentRelease(stop.PersistentIds).Count);
        Assert.IsFalse(registry.HasOwnedIds);
    }

    [TestMethod]
    public void DisabledRetentionKeepsExistingImmediateDisconnectBehavior()
    {
        HidHideManagedDeviceRegistry<object> registry = new();
        registry.OpenLifecycle(keepHiddenOnReconnect: false);
        var source = new object();
        var claim = registry.BeginConnection(source, new[] { Owned });
        registry.CompleteConnection(claim, Array.Empty<string>(), new[] { Owned });
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.Disconnect(source).PersistentReleaseIds.ToArray());
    }

    [TestMethod]
    public void CancelledConnectionStillRollsBackItsNewEntries()
    {
        HidHideManagedDeviceRegistry<object> registry = new();
        registry.OpenLifecycle(keepHiddenOnReconnect: true);
        var claim = registry.BeginConnection(new object(), new[] { Owned });
        registry.CancelConnection(claim);
        CollectionAssert.AreEquivalent(new[] { Owned },
            registry.CompleteConnection(claim, Array.Empty<string>(), new[] { Owned }).ToArray());
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.RevalidatePersistentRelease(new[] { Owned }).ToArray());
    }

    [TestMethod]
    public void LateReleaseReassertsRetainedIdentityAndStopReleasesBothTransports()
    {
        HidHideManagedDeviceRegistry<object> registry = new();
        registry.OpenLifecycle(keepHiddenOnReconnect: true);
        foreach (string id in new[] { Owned, "USB\\SECOND" })
        {
            var source = new object();
            var claim = registry.BeginConnection(source, new[] { id });
            registry.CompleteConnection(claim, Array.Empty<string>(), new[] { id });
            registry.Disconnect(source);
        }
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.CompletePersistentRelease(new[] { Owned }).ToArray());
        var stop = registry.BeginServiceRelease();
        CollectionAssert.AreEquivalent(new[] { Owned, "USB\\SECOND" }, stop.PersistentIds.ToArray());
        registry.CompletePersistentRelease(stop.PersistentIds);
        registry.OpenLifecycle(keepHiddenOnReconnect: false);
        Assert.IsFalse(registry.HasOwnedIds);
        var fresh = new object();
        var next = registry.BeginConnection(fresh, new[] { Owned });
        registry.CompleteConnection(next, Array.Empty<string>(), new[] { Owned });
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.Disconnect(fresh).PersistentReleaseIds.ToArray());
    }

    [TestMethod]
    public void FailedStopReopenPreservesRetentionPolicyAndOwnedIds()
    {
        HidHideManagedDeviceRegistry<object> registry = new();
        registry.OpenLifecycle(keepHiddenOnReconnect: true);
        var source = new object();
        var claim = registry.BeginConnection(source, new[] { Owned });
        registry.CompleteConnection(claim, Array.Empty<string>(), new[] { Owned });
        registry.Disconnect(source);
        registry.CloseLifecycle();
        registry.OpenLifecycle(); // production Stop failure resumes the same policy
        Assert.AreEqual(0, registry.RevalidatePersistentRelease(new[] { Owned }).Count);
        CollectionAssert.AreEquivalent(new[] { Owned }, registry.BeginServiceRelease().PersistentIds.ToArray());
    }

    [TestMethod]
    [DataRow(null, true)]
    [DataRow("False", false)]
    [DataRow("True", true)]
    [DataRow("invalid", true)]
    public void PreferenceDefaultsOnAndRoundTripsWithoutChangingOtherSettings(string value, bool expected)
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        string xml = "<Profile>" + (value == null ? "" :
            "<KeepControllersHiddenOnReconnect>" + value + "</KeepControllersHiddenOnReconnect>") + "</Profile>";
        using var reader = new StringReader(xml);
        var dto = (AppSettingsDTO)serializer.Deserialize(reader)!;
        var store = new BackingStore();
        dto.MapTo(store);
        Assert.AreEqual(expected, store.keepControllersHiddenOnReconnect);
        store.keepControllersHiddenOnReconnect = !expected;
        var saved = new AppSettingsDTO();
        saved.MapFrom(store);
        using var writer = new StringWriter();
        serializer.Serialize(writer, saved);
        StringAssert.Contains(writer.ToString(), "<KeepControllersHiddenOnReconnect>" + (!expected) + "</KeepControllersHiddenOnReconnect>");
    }
}
