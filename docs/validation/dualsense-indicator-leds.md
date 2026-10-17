# DualSense indicator LEDs: issue #40 validation

Use a physical DualSense with the candidate build from `feature/profile-indicator-leds`.
This change adds **Profile Editor > Lightbar > Indicator LEDs**. It is independent
of the RGB lightbar's Normal/Pass-through setting. DualSense Edge uses the same
output path, but should be recorded separately if tested.

## Prepare

1. Keep the released build and your normal profiles as a backup. Extract the
   complete candidate portable package into a separate folder. Close DS4Windows
   before switching builds; run only one copy at a time. No driver update is needed.
2. Make a copy of your usual profile for this test. Close games and other
   controller/lighting tools. Start with the DualSense connected by **USB**.
3. In the candidate, select the test profile, open **Lightbar**, set **White player
   lights** to **Off**, and click **Apply**. Leave **Orange microphone light** on
   **Device settings** for the first test. Confirm the white lights are off.
4. Open [DualSense Tester](https://ds.daidr.me/) in Chrome or Edge. Its English UI
   calls the control **Player light**, with an **All** choice. Connect/select the
   **physical** DualSense in the browser's device picker, rather than an emulated
   controller. Leave DS4Windows running.
5. If HidHide prevents the browser from seeing the physical controller, temporarily
   allow the browser executable in HidHide's Applications list, then remove that
   allowance after testing. If the browser still cannot control the physical
   lights while DS4Windows is running, record that result; restarting DS4Windows
   between lighting and clearing is a different test.

## Reproduce and clear the lights

1. In the tester, choose **Player light > All**. Check that the five **white lights
   below the touchpad** physically illuminate. The coloured lightbar is separate.
2. **Close the tester tab**, leaving DS4Windows running. Wait about two seconds.
   Record whether the white lights remain on. Do not switch profiles, reconnect
   the controller, or change any other setting.
3. In **Profile Editor > Lightbar > Indicator LEDs**, leave **White player lights:
   Off** selected and click **Reapply indicators**. Expected: the white lights
   turn off promptly (normally within about a second), without a profile switch
   or controller reconnect. This button previews the current editor selections;
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
- Open an older profile that has neither new setting. Both controls should show
  **Device settings** and preserve its previous controller-specific behaviour.
  Returning from an explicit override to Device settings should restore that
  controller's existing policy, including native-game control where applicable.
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
native underlay preservation, and DSX/microphone mute priorities. Physical LED
behaviour and the browser reproduction require the manual steps above.
