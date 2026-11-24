using DS4Windows;
using DS4WinWPF.DS4Control;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests
{
    [TestClass]
    public class DebouncerRealtimeTests
    {
        [TestMethod]
        public void TemporaryProfileAndPerButtonOverridesTakeEffectOnTheNextReport()
        {
            var debouncer = new Debouncer(TimeSpan.Zero);
            debouncer.AddDebouncer(nameof(DS4State.Cross));
            debouncer.AddDebouncer(nameof(DS4State.Circle));
            var state = new DS4State { Cross = true, Circle = true, ReportTimeStamp = new DateTime(10_000_000) };
            var targetOverrides = new Dictionary<DS4Controls, int> { [DS4Controls.Circle] = 0 };
            debouncer.ApplyProfile(100, targetOverrides);
            Assert.IsTrue(debouncer.ProcessInput(state).Cross);
            state.ReportTimeStamp = state.ReportTimeStamp.AddMilliseconds(1);
            state.Cross = state.Circle = false;
            DS4State target = debouncer.ProcessInput(state);
            Assert.IsTrue(target.Cross);
            Assert.IsFalse(target.Circle, "An explicit zero bypasses the inherited duration.");

            debouncer.ApplyProfile(0, null);
            Assert.IsFalse(debouncer.ProcessInput(state).Cross, "Returning to the parent clears the target hold.");
            var parentOverrides = new Dictionary<DS4Controls, int> { [DS4Controls.Cross] = 50 };
            debouncer.ApplyProfile(0, parentOverrides);
            state.Cross = true;
            Assert.IsTrue(debouncer.ProcessInput(state).Cross);
            state.Cross = false;
            Assert.IsTrue(debouncer.ProcessInput(state).Cross, "Overrides also work when the global duration is zero.");
        }

        [TestMethod]
        public void ProfileXmlRoundTripsOverridesAndLegacyProfilesInherit()
        {
            var original = new BackingStore();
            original.debouncingMs[0] = 100;
            original.buttonDebounceOverrides[0] = new Dictionary<DS4Controls, int>
                { [DS4Controls.Cross] = 0, [DS4Controls.Circle] = 20 };
            var dto = new ProfileDTO { DeviceIndex = 0 };
            dto.MapFrom(original);
            using var writer = new StringWriter();
            ProfileDTO.Serializer.Serialize(writer, dto);
            using var reader = new StringReader(writer.ToString());
            var decoded = (ProfileDTO)ProfileDTO.Serializer.Deserialize(reader);
            decoded.DeviceIndex = 0;
            var destination = new BackingStore();
            decoded.MapTo(destination);
            Assert.AreEqual(100, destination.debouncingMs[0]);
            Assert.AreEqual(0, destination.buttonDebounceOverrides[0][DS4Controls.Cross]);
            Assert.AreEqual(20, destination.buttonDebounceOverrides[0][DS4Controls.Circle]);
            decoded.ButtonDebounceOverrides = null;
            decoded.MapTo(destination);
            Assert.IsNull(destination.buttonDebounceOverrides[0]);
        }

        [TestMethod]
        public void EnabledDebouncerPreservesTypedBehaviorAndAllocatesZero()
        {
            Debouncer debouncer = new(TimeSpan.FromMilliseconds(10));
            debouncer.AddDebouncer(nameof(DS4State.Cross));
            debouncer.AddDebouncer(nameof(DS4State.R2Btn));
            DS4State source = new()
            {
                Cross = true,
                R2Btn = true,
                R2 = 211,
                LX = 17,
                ReportTimeStamp = new DateTime(10_000_000),
            };

            DS4State pressed = debouncer.ProcessInput(source);
            Assert.IsTrue(pressed.Cross);
            Assert.IsTrue(pressed.R2Btn);
            Assert.AreEqual(211, pressed.R2);
            Assert.AreEqual(17, pressed.LX);
            source.Cross = false;
            source.R2Btn = false;
            source.ReportTimeStamp = source.ReportTimeStamp.AddMilliseconds(5);
            DS4State held = debouncer.ProcessInput(source);
            Assert.IsTrue(held.Cross);
            Assert.IsTrue(held.R2Btn);
            source.ReportTimeStamp = source.ReportTimeStamp.AddMilliseconds(6);
            DS4State released = debouncer.ProcessInput(source);
            Assert.IsFalse(released.Cross);
            Assert.IsFalse(released.R2Btn);

            for (int index = 0; index < 512; index++)
            {
                source.ReportTimeStamp = source.ReportTimeStamp.AddTicks(1);
                _ = debouncer.ProcessInput(source);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10_000; index++)
            {
                source.ReportTimeStamp = source.ReportTimeStamp.AddTicks(1);
                _ = debouncer.ProcessInput(source);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
        }
    }
}
