# Stick-based gyro activation: test guide

Implements the stick-movement behaviour discussed in [upstream issue 14](https://github.com/hbashton/DS4Windows/issues/14). This is separate from the Bluetooth haptics candidate.

## Setting

Open **Profile Editor > Gyro > Stick-based gyro activation**.

- **Off** preserves the existing behaviour and is the default for old profiles.
- **Enable while stick is moving** allows gyro when the selected stick moves beyond the threshold.
- **Pause while stick is moving** pauses gyro beyond the threshold and restores it near the centre.
- Select **Left stick**, **Right stick**, or **Either stick**. Either means movement of at least one stick.
- The default movement threshold is **15%** of stick travel. The gate releases below **13%** to avoid repeatedly switching near the boundary. This is independent of the stick's output deadzone.

This condition combines with existing gyro activation buttons for mapped gyro aiming; it does not replace them. L3/R3 clicks do not count as stick movement. The setting also pauses angular velocity sent to virtual DS4, DualSense/Edge and Switch 2 controllers. It preserves physical sensor data, accelerometer/gravity, buttons and stick output. UDP/DSU motion is unchanged. Games which use accelerometer tilt directly may still react to tilt while angular velocity is paused.

## DualSense hardware checks

Use a copy of your normal profile in the candidate portable build. Close other DS4Windows copies first. No driver update is needed.

1. Start with **Off** and confirm your existing gyro aiming works. Select a suitable output mode or test native motion in a game which supports your virtual DS4/DualSense. Disable competing Steam Input gyro mapping for this comparison.
2. Select **Right stick**, **Enable while stick is moving**, **15%**. Save. With the right stick centred, rotate the controller: mapped aiming/native gyro should stay paused. Move the right stick well beyond the threshold and repeat: gyro should work. Return to centre: it should stop promptly. Stick movement itself still reaches the game normally.
3. Select **Pause while stick is moving**. Save. Repeat: gyro should now work at centre, pause during movement and resume at centre without replaying motion from the paused interval.
4. Check **Left stick** and **Either stick**. The unselected stick should not affect the gate. For Either, gyro should remain in the moving state until both sticks return near centre. Clicking centred L3/R3 should not activate movement.
5. Move slowly around the threshold. Small drift should not cause rapid on/off changes. Raise the threshold if your stick drifts. Test diagonal movement too.
6. If you use an activation button, verify it still needs to be held/toggled as configured. The stick setting must not bypass it or change its toggle state. For a simple first test, use Always On in the mapped aiming controls.
7. Save, close and reopen the profile. Confirm the three settings persist. Switch to an old profile: its stick gate should be Off. Reconnect the controller and repeat a centre/movement check over USB and Bluetooth.
8. Check normal buttons, sticks, haptics and adaptive triggers. If you use UDP/DSU motion, confirm it continues independently of this setting.

Report the connection type, selected virtual output, gyro output mode, three new settings and any failing step. Native-game motion and DS4Windows mouse/stick aiming are different paths; please identify which one you tested.
