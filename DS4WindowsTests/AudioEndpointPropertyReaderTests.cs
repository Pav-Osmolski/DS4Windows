using DS4Windows;
using NAudio.CoreAudioApi;

namespace DS4WindowsTests;

[TestClass]
public class AudioEndpointPropertyReaderTests
{
    [TestMethod]
    public void LargeStoresAreScannedOnceAndOnlyRequestedValuesAreRead()
    {
        var keys = Enumerable.Range(0, 500).Select(i => new PropertyKey(Guid.Empty, i)).ToArray();
        int keyReads = 0, valueReads = 0;
        var values = AudioEndpointPropertyReader.Read(keys.Length,
            i => { keyReads++; return keys[i]; },
            i => { valueReads++; return "value-" + i; }, keys[123], keys[450]);
        Assert.AreEqual(451, keyReads, "Stop once all requested keys are found; never rescan per field.");
        Assert.AreEqual(2, valueReads, "Unrelated properties must never be read.");
        Assert.AreEqual("value-123", values[keys[123]]);
        Assert.AreEqual("value-450", values[keys[450]]);
    }

    [TestMethod]
    public void MissingAndStaleFieldsDoNotHideOtherIdentityProperties()
    {
        var keys = Enumerable.Range(0, 4).Select(i => new PropertyKey(Guid.Empty, i)).ToArray();
        var values = AudioEndpointPropertyReader.Read(3,
            i => i == 0 ? throw new InvalidOperationException("Stale key") : keys[i],
            i => i == 1 ? throw new InvalidOperationException("Stale value") : "valid",
            keys);
        Assert.AreEqual(1, values.Count);
        Assert.AreEqual("valid", values[keys[2]]);
        Assert.IsFalse(values.ContainsKey(keys[3]));
    }

    [TestMethod]
    public void ANewReadObservesChangedPropertiesWithoutReusingOldIdentity()
    {
        var key = PropertyKeys.PKEY_Device_InterfaceKey;
        string current = "old-interface";
        var before = AudioEndpointPropertyReader.Read(1, _ => key, _ => current, key);
        current = "replacement-interface";
        var after = AudioEndpointPropertyReader.Read(1, _ => key, _ => current, key);
        Assert.AreEqual("old-interface", before[key]);
        Assert.AreEqual("replacement-interface", after[key]);
    }
}
