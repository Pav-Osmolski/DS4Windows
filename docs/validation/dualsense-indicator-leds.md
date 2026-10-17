# DualSense indicator LEDs: issue #40 validation

Use a physical DualSense with the candidate build from `feature/profile-indicator-leds`.
This change adds **Profile Editor > Lightbar > Indicator LEDs** and extends the
existing **Passthru** output mode to both indicators. DualSense Edge uses the same
output path, but should be recorded separately if tested.

| Lightbar output | Indicator selection | Expected control |
| --- | --- | --- |
| Normal | Device settings | Existing controller-specific LED behaviour; the microphone light defaults to Off. |
| Passthru | Device settings | Latest game/application LED request; device settings are the fallback until a request arrives. |
| Either | Off / player pattern / On / Pulse / battery | Explicit profile selection overrides native application requests. |

Microphone mute indication and explicit DSX commands retain their priority.
Returning to **Device settings in Passthru** restores the latest remembered
application request, even if it was sent only once while a profile override was
active. An application player-LED release returns to the device policy. Normal
mode retains the existing behaviour; it is not microphone-light pass-through.

## Prepare

1. Keep the released build and your normal profiles as a backup. Extract the
   complete candidate portable package into a separate folder. Close DS4Windows
   before switching builds; run only one copy at a time. No driver update is needed.
2. Make a copy of your usual profile for this test. Close games and other
   controller/lighting tools. Start with the DualSense connected by **USB**.
3. For the forwarding test below, set **Emulated Controller: DualSense**. In
   **Lightbar**, select **Output Mode: Passthru** and set both **White player
   lights** and **Orange microphone light** to **Device settings**. Click Apply.
4. Open [DualSense Tester](https://ds.daidr.me/) in Chrome or Edge and connect the
   **virtual DualSense**. This exercises browser -> emulated DualSense ->
   DS4Windows -> physical controller. Its English controls are **Player light**
   (including **All**) and **Mic status light** (Off / On / Blink).
5. Keep DS4Windows running throughout. A USB icon in the tester can describe the
   virtual device, even while your physical controller uses Bluetooth. Record
   the physical connection reported by DS4Windows when comparing USB/Bluetooth.

## Passthru and return from overrides

1. With **Passthru** and both indicators on **Device settings**, set the tester's
   **Player light: All** and **Mic status light: Blink**. Expected: all five white
   lights illuminate and the orange light pulses. Try On as well.
2. In DS4Windows set both indicator selections to **Off**, then click **Reapply
   indicators**. Expected: both lights turn off. Do not change the tester controls.
3. Return both selections to **Device settings**, then Reapply. Expected: all five
   white lights and the previous microphone-light state return, without another
   website command. Repeat steps 2-3 using ordinary **Apply** instead of Reapply.
4. While the DS4Windows Off overrides are active, change the tester to another
   player pattern and change the microphone light from Blink to On. Return to
   Device settings and Reapply. Expected: the **newest** application requests
   appear, rather than the older All/Blink state.
5. Send Off from the tester while both indicators use Device settings. Expected:
   both turn off and stay off; a valid zero must not revive an older On request.
6. Set Blink in the tester again. Change lightbar output to **Normal** and Apply.
   The inherited microphone light should return to your device policy (Off by
   default). Explicit On/Pulse should still work in Normal. Return to Device
   settings and **Passthru**, Apply, and verify the application's remembered Blink
   returns. This distinguishes device defaults from application forwarding.
7. Close the tester tab, then repeat an Off -> Device settings transition in
   Passthru. Expect the latest remembered state, unless the browser/site sent a
   final Off or release before closing. Record that separately from the steps
   where the tab stays open.
8. Repeat over physical **Bluetooth**, with USB disconnected. Also check changing
   an indicator while speaker/microphone audio is active if that is your normal
   setup. Do not reconnect or switch profiles between overriding and restoring.

## Original issue: lights left by a separate physical-device writer

This is a separate test from virtual-controller forwarding. The browser must
reach the **physical DualSense directly** while DS4Windows stays running. Using
Xbox 360 emulation can help avoid selecting a virtual DualSense accidentally,
but it does not make a hidden physical controller visible.

If HidHide prevents the browser seeing the physical controller, temporarily allow
the browser executable in HidHide's Applications list, then remove that allowance
after testing. If only DualSense emulation makes the tester detect a controller,
use the forwarding test above and report this direct-access test as unavailable.

Select **White player lights: Off** in the test profile and Apply before starting.

1. In the tester, choose **Player light > All**. Check that the five **white lights
   below the touchpad** physically illuminate. The coloured lightbar is separate.
2. **Close the tester tab**, leaving DS4Windows running. Wait about two seconds.
   Record whether the white lights remain on. Do not switch profiles, reconnect
   the controller, or change any other setting.
3. In **Profile Editor > Lightbar > Indicator LEDs**, leave **White player lights:
   Off** selected and click **Reapply indicators**. Expected: the white lights
   turn off promptly (normally within about a second), without a profile switch
   or controller reconnect. This button applies the current editor selections;
   use Apply/Save to persist them.
4. Repeat steps 1–2. This time click the profile editor's **Apply** button with
   **Off still selected**. Expected: the lights turn off even though the requested
   value has not changed. Repeat once using **Save** if available in your editing
   flow.
5. Repeat with the lightbar's output mode set to **Pass-through**, then return to
   your normal mode. The player-light control should work in either mode.
6. Repeat the same sequence over **Bluetooth**, with the USB cable disconnected.
   Record the two connection types separately.

If DS4Windows clears the lights before you can close the tester tab, record that
instead. Do not interpret a test where the browser never illuminates the physical
lights as a successful reproduction. Another application still writing lighting
commands can compete with DS4Windows; this test deliberately closes that writer.

## Other checks

- Try **Player 1–4**, **All five lights**, and **Off**, pressing Reapply after each.
  The player patterns are respectively centre, the inner pair, three alternating
  lights, and four outer lights. All five lights illuminates every indicator.
- Try **Battery level**. It uses five/four/three/two/one/zero lights at
  95/70/50/20/5/below-5 percent, and updates when a new battery reading arrives.
  There is no need to discharge the controller just to validate this change.
- Try the orange microphone indicator's **Off**, **On**, and **Pulse** choices.
  These change the light, not microphone mute. If your profile assigns microphone
  mute controls, verify their existing status indication still takes priority.
  Active DSX indicator commands also retain their existing priority.
- Save, switch to another profile, and switch back. Verify the two selections
  persist independently. Restart DS4Windows and check them again.
- Open an older Normal-mode profile that has neither new setting. Both controls
  should show **Device settings** and preserve its previous controller-specific
  behaviour. In Passthru, inherited indicators now follow application requests.
- Confirm RGB lightbar, adaptive triggers, ordinary rumble, speaker/microphone
  audio, and reconnect still work. A controller of another type should continue
  to work normally; Reapply explains that a DualSense is required.
- Check the new section in light/dark themes at your usual display scaling and
  with a narrower editor window. Labels should remain centred beside the fields.

## Compare with the release and report results

Repeat the browser-lighting sequence on the released build with its existing
device-specific player LEDs set to Off. Record whether closing the browser leaves
the lights on and whether switching profiles clears them. Then repeat on the
candidate using the unchanged Off selection and Reapply/Apply above. Keep the
same controller, connection, browser and test profile where possible.

Report controller model, USB/Bluetooth, DS4Windows version/build, browser, whether
the physical lights stayed on after closing the tab, and whether **Reapply** and
**Apply** each cleared them. Include logs if output failed or the controller
disconnected. A brief video is useful if a light returns after clearing. If the
released build does not reproduce the problem, the candidate result establishes
compatibility, not proof that the original issue is fixed.

## Automated coverage

`ProfileIndicatorTests` checks old-profile defaults, XML round trips, invalid
in-memory values, battery patterns, fresh publication of unchanged Off, actual
USB/Bluetooth report preparation and validity flags, native-report composition,
native underlay preservation, and DSX/microphone mute priorities. Native ingress
regressions also cover USB browser commands, mic-only validity-masked deltas,
restoring both indicators after Off without a new application packet, live output
mode changes, valid zero values, and the Bluetooth combined-report compositor.
Physical LED behaviour and the browser reproduction require the manual steps above.
