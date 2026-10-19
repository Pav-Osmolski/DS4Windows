using System.IO;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests
{
    [TestClass]
    public class BluetoothPollingProfileTests
    {
        private static ProfileDTO Read(string elements)
        {
            var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
            using var reader = new StringReader("<DS4Windows>" + elements + "</DS4Windows>");
            return (ProfileDTO)serializer.Deserialize(reader);
        }

        [TestMethod]
        public void NewAndMissingRateProfilesUseAutomaticConservativeInterval()
        {
            var store = new BackingStore();
            Assert.IsTrue(store.automaticBTPollRate[0]);
            Assert.AreEqual(4, store.GetEffectiveBTPollRate(0));
            Assert.IsTrue(Read("").AutomaticBTPollRate);
            Assert.IsTrue(new ProfileDTO().AutomaticBTPollRate);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(4)]
        [DataRow(16)]
        public void OldProfilesKeepManualRequestIncludingLegacyMax(int rate)
        {
            ProfileDTO dto = Read($"<BTPollRate>{rate}</BTPollRate>");
            Assert.IsFalse(dto.AutomaticBTPollRate);
            Assert.AreEqual(rate, dto.BTPollRateString);
        }

        [TestMethod]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public void ExplicitModeDoesNotDependOnXmlElementOrder(bool automatic, bool modeFirst)
        {
            string mode = $"<AutomaticBTPollRate>{automatic.ToString().ToLowerInvariant()}</AutomaticBTPollRate>";
            const string rate = "<BTPollRate>2</BTPollRate>";
            ProfileDTO dto = Read(modeFirst ? mode + rate : rate + mode);
            Assert.AreEqual(automatic, dto.AutomaticBTPollRate);
            Assert.AreEqual(2, dto.BTPollRateString);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void SavedModeAndManualChoiceRoundTripThroughRuntimeStore(bool automatic)
        {
            var source = new BackingStore();
            source.automaticBTPollRate[0] = automatic;
            source.btPollRate[0] = 2;
            var dto = new ProfileDTO { DeviceIndex = 0 };
            dto.MapFrom(source);
            var serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
            using var writer = new StringWriter();
            serializer.Serialize(writer, dto);
            using var reader = new StringReader(writer.ToString());
            var restored = (ProfileDTO)serializer.Deserialize(reader);
            restored.DeviceIndex = 0;
            var destination = new BackingStore();
            restored.MapTo(destination);
            Assert.AreEqual(automatic, destination.automaticBTPollRate[0]);
            Assert.AreEqual(2, destination.btPollRate[0]);
            Assert.AreEqual(automatic ? 4 : 2, destination.GetEffectiveBTPollRate(0));
            destination.automaticBTPollRate[0] = false;
            Assert.AreEqual(2, destination.GetEffectiveBTPollRate(0), "Automatic must retain the user's manual choice.");
        }
    }
}
