using DS4Windows;
using NAudio.CoreAudioApi;

namespace DS4WindowsTests
{
    [TestClass]
    public class ViiperAudioEndpointNamesTests
    {
        [TestMethod]
        public void NamesAreDistinctByControllerAndDirection()
        {
            var names = new HashSet<string>();
            for (int i = 0; i < ControlService.MAX_DS4_CONTROLLER_COUNT; i++)
            {
                Assert.IsTrue(names.Add(ViiperAudioEndpointNames.NameFor(i, DataFlow.Render)));
                Assert.IsTrue(names.Add(ViiperAudioEndpointNames.NameFor(i, DataFlow.Capture)));
            }
            Assert.AreEqual("DS4Windows Controller 1 Microphone",
                ViiperAudioEndpointNames.NameFor(0, DataFlow.Capture));
        }

        [TestMethod]
        public void OnlyOwnedVirtualPortIsRenamedEvenWithIdenticalSonyNames()
        {
            var endpoints = new[]
            {
                new ViiperAudioEndpointNames.Endpoint("physical", "Headset Microphone (Wireless Controller)", DataFlow.Capture, -1),
                new ViiperAudioEndpointNames.Endpoint("another-controller", "Headset Microphone (Wireless Controller)", DataFlow.Capture, 2),
                new ViiperAudioEndpointNames.Endpoint("owned-microphone", "Headset Microphone (Wireless Controller)", DataFlow.Capture, 1),
                new ViiperAudioEndpointNames.Endpoint("owned-speakers", "Speakers (Wireless Controller)", DataFlow.Render, 1),
            };
            var writes = new Dictionary<string, string>();
            Assert.AreEqual(3, ViiperAudioEndpointNames.ApplyNames(endpoints, 1, 3,
                () => true, (id, name) => writes.Add(id, name)));
            Assert.AreEqual(2, writes.Count);
            Assert.AreEqual("DS4Windows Controller 4 Microphone", writes["owned-microphone"]);
            Assert.AreEqual("DS4Windows Controller 4 Audio", writes["owned-speakers"]);
        }

        [TestMethod]
        public void RetiredOwnerCannotRenameSuccessorEndpoint()
        {
            bool current = true;
            var writes = new List<string>();
            var endpoints = new[]
            {
                new ViiperAudioEndpointNames.Endpoint("first", "old", DataFlow.Render, 1),
                new ViiperAudioEndpointNames.Endpoint("second", "old", DataFlow.Capture, 1),
            };
            ViiperAudioEndpointNames.ApplyNames(endpoints, 1, 0, () => current,
                (id, name) => { writes.Add(id); current = false; });
            CollectionAssert.AreEqual(new[] { "first" }, writes);
        }

        [TestMethod]
        public void ExistingNamesAreNotRewrittenAndInvalidPortNeverMatches()
        {
            var endpoints = new[]
            {
                new ViiperAudioEndpointNames.Endpoint("capture", ViiperAudioEndpointNames.NameFor(0, DataFlow.Capture), DataFlow.Capture, 1),
                new ViiperAudioEndpointNames.Endpoint("render", ViiperAudioEndpointNames.NameFor(0, DataFlow.Render), DataFlow.Render, 1),
            };
            Assert.AreEqual(3, ViiperAudioEndpointNames.ApplyNames(endpoints, 1, 0,
                () => true, (_, _) => Assert.Fail("Unexpected property write")));
            Assert.AreEqual(0, ViiperAudioEndpointNames.ApplyNames(endpoints, -1, 0,
                () => true, (_, _) => Assert.Fail("Unowned endpoint")));
        }

        [TestMethod]
        public void NamedRenderEndpointStillUsesControllerGameAudioPolicy()
        {
            Assert.IsTrue(DualSenseBluetoothSpeakerPassthrough.IsLikelyGameAudioEndpoint(
                ViiperAudioEndpointNames.NameFor(0, DataFlow.Render)));
            Assert.IsFalse(DualSenseBluetoothSpeakerPassthrough.IsLikelyGameAudioEndpoint("Desktop speakers"));
        }

        [TestMethod]
        public void RenameFailureDoesNotReportSuccess()
        {
            var endpoints = new[] { new ViiperAudioEndpointNames.Endpoint("capture", "old", DataFlow.Capture, 1) };
            Assert.ThrowsExactly<System.Runtime.InteropServices.COMException>(() =>
                ViiperAudioEndpointNames.ApplyNames(endpoints, 1, 0, () => true,
                    (_, _) => throw new System.Runtime.InteropServices.COMException("Access denied")));
        }
    }
}
