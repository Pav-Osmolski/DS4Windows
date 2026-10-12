using System.Reflection;
using System.Runtime.CompilerServices;
using DS4Windows;

namespace DS4WindowsTests;

[TestClass]
[DoNotParallelize]
public sealed class GameBarCompatibilityAdmissionTests
{
    [TestMethod]
    [DataRow(false, OutContType.ViiperDualSense, false)]
    [DataRow(true, OutContType.ViiperXboxOne, false)]
    [DataRow(true, OutContType.ViiperX360, false)]
    [DataRow(true, OutContType.ViiperDualSense, true)]
    public void DelayedActivationRechecksCurrentProfileBeforeTouchingOutput(
        bool enabled, OutContType requestedOutput, bool dInputOnly)
    {
        const int slot = 0;
        bool previousEnabled = Global.GameBarControllerCompatibility[slot];
        OutContType previousOutput = Global.OutContType[slot];
        bool previousDInputOnly = Global.DinputOnly[slot];
        try
        {
            var service = (ControlService)RuntimeHelpers.GetUninitializedObject(typeof(ControlService));
            var source = (DS4Device)RuntimeHelpers.GetUninitializedObject(typeof(DS4Device));
            var native = new HeldOutput();
            var companions = new OutputDevice[1];
            var routes = new int[1];
            var retries = new DateTime[1];
            service.running = true;
            service.DS4Controllers = new[] { source };
            service.outputDevices = new OutputDevice[] { native };
            Set(service, "gameBarCompatibilityOutputLock", new object());
            Set(service, "gameBarCompatibilityOutputDevices", companions);
            Set(service, "gameBarCompatibilityRoutingActive", routes);
            Set(service, "gameBarCompatibilityNextRetryUtc", retries);
            // No manager, driver, or device worker exists in this fixture.
            // Reaching slot allocation before policy rejection fails the test.
            Action<int> activate = typeof(ControlService)
                .GetMethod("ActivateGameBarCompatibilityOutput", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<int>>(service);

            Global.GameBarControllerCompatibility[slot] = true;
            Global.OutContType[slot] = OutContType.ViiperDualSense;
            Global.DinputOnly[slot] = false;
            Assert.IsTrue(ControlService.ShouldUseGameBarControllerCompatibility(
                Global.GameBarControllerCompatibility[slot], Global.OutContType[slot],
                Global.getDInputOnly(slot)));
            Action delayedActivation = () => activate(slot);

            // The caller already selected activation; the profile changes before
            // the queued work enters the real service activation method.
            Global.GameBarControllerCompatibility[slot] = enabled;
            Global.OutContType[slot] = requestedOutput;
            Global.DinputOnly[slot] = dInputOnly;
            delayedActivation();

            Assert.AreSame(native, service.outputDevices[slot]);
            Assert.AreEqual(255, native.StickX);
            Assert.AreEqual(0, native.ResetCount);
            Assert.IsNull(companions[slot]);
            Assert.AreEqual(0, routes[slot]);
            Assert.AreEqual(DateTime.MinValue, retries[slot]);
        }
        finally
        {
            Global.GameBarControllerCompatibility[slot] = previousEnabled;
            Global.OutContType[slot] = previousOutput;
            Global.DinputOnly[slot] = previousDInputOnly;
        }
    }

    private static void Set(ControlService service, string name, object value) =>
        typeof(ControlService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, value);

    private sealed class HeldOutput : OutputDevice
    {
        internal int StickX = 255;
        internal int ResetCount;
        public override void ResetState(bool submit = true) { ResetCount++; StickX = 128; }
        public override string GetDeviceType() => OutContType.ViiperXboxOne.ToString();
        public override void Connect() => Assert.Fail("No output should be connected.");
        public override void Disconnect() => Assert.Fail("The native output must remain connected.");
        public override void ConvertandSendReport(DS4State state, int device) =>
            Assert.Fail("Policy rejection must not send a report.");
        public override void RemoveFeedbacks() => Assert.Fail("Feedback must remain attached.");
        public override void RemoveFeedback(int inIdx) => Assert.Fail("Feedback must remain attached.");
    }
}
