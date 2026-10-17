# Profile Editor field alignment: visual checklist

Test the `fix/dark-dropdown-popup` build in both Light and Dark themes, using your normal Windows display scale. Open the Profile Editor and check the areas below. Expand the relevant settings and select each output mode to reveal conditional rows. Some Advanced audio/feedback controls require a suitable controller/output.

For each row, check that its name is vertically centred beside its field and that adjacent units or readouts line up. For the two Snap to Center options, check the checkbox square itself against the text. Click a few controls to confirm normal editing still works.

## Advanced

| Section | Changed rows |
| --- | --- |
| Output | Emulated Controller; DS4 Trigger Output Mode |
| Feedback | Rumble mode and strength level, beside both dropdowns |
| Input: Mouse and touchpad | Mouse Sensitivity; Mouse Vertical Scale; Mouse Offset; their percent units |
| Input: Absolute Mouse Options > Settings | Width; Height; X Center; Y Center; Anti Radius; Snap To Center checkbox |
| Input: filtering/readout | Debouncing's ms unit; Use Controller and the adjacent “for readout” checkbox |
| Connection | DS4 BT Poll Rate; idle-disconnect mins unit |
| Audio | Audio source; USB DualSense speaker; Speaker volume; Dynamic range; Bass/body boost; Microphone level; Noise suppression; Headset / AUX volume; Controller mic; Virtual output; Mute on profile; Mute off profile |

## Other Profile Editor pages

| Page | Changed rows |
| --- | --- |
| Axis Config: both sticks | Output Mode; Dead Zone Type; Dead Zone; Max Zone; Anti-dead Zone; Max Output; Vertical Scale; Sensitivity; Output Curve; Square Stick; Rotation; Fuzz; Anti Snapback and timing; Outer Btn Dead |
| Axis Config: trigger/axis tuning | Multiplier; Max Travel; Min Travel; Easing Duration; Min Factor, wherever shown |
| Axis Config: Flick Stick | Real World Calibration; Flick Threshold; Flick Time; Min Angle Threshold; Rotation, for both sticks |
| Lightbar | Output Mode; low-battery percent unit; While Charging; rainbow secs/cycle unit |
| Touchpad: relative mouse | Output Mode; Rotation; Min Threshold; Friction; Invert; Disable Invert |
| Touchpad: controls | Assigned-action names beside the four swipe buttons |
| Touchpad: stick output | Dead Zone; Max Zone; Output Stick; Output Axes; Minimum X/Y and percent units; Vertical Scale; Friction; Max Output percent unit; Output Curve; Rotation; smoothing MinCutoff/Beta |
| Touchpad: absolute mouse | Max Zone X/Y; Snap To Center on Release checkbox |
| Gyro: all applicable modes | Output Mode; assigned-action names beside swipe buttons; Triggers; Eval Cond.; steering wheel axis/range; smoothing MinCutoff/Beta; Gyro Sensitivity; Vertical Scale and percent units; Invert; Dead Zone; Min Threshold; Smooth Weight |
| Gyro: stick aiming | Max Zone; Output Stick; Output Axes; Minimum X/Y and percent units; Max Output percent unit; X-Axis; smoothing Weight |
| Gyro: controls/swipes | Fuzz; Dead Zone X/Y and deg/sec units; Delay Time and ms unit |

Nintendo Options uses different field rows; its field names already matched their editors, but the follow-up below corrects two feedback unit suffixes. Headings, captions above fields, and assignment summaries above buttons retain their existing spacing.

## Follow-up: nested controls and unit suffixes

Check these additional fixes in both themes:

- **Touchpad > Button activation:** Button Activation Mode is a separate shared control, used in the different Touchpad output modes. Its label is now centred beside the dropdown.
- **Axis Config > Left stick and Right stick:** check Max Output's Force checkbox/text, Square Stick's bare checkbox, and Anti Snapback's bare checkbox. The checkbox marks and captions are centred relative to the numeric fields, including the fields' existing bottom spacing.
- **Axis Config > both sticks > Outer-stick action:** Outer Btn aligns with its binding button. Outer Btn Dead aligns with its numeric field; Invert has its own row below, so it no longer affects the label's centre.
- **Axis Config > both sticks > Axial:** the shared X/Y table's Dead Zone, Max Zone, Anti-dead Zone and Max Output labels are centred with their fields.
- **Unit suffixes:** check percent, degrees, units, milliseconds, seconds and minutes beside fields in Axis Config, Touchpad, Gyro, Lightbar and Advanced. Suffixes have at least 3px of left clearance and use the same vertical spacing as their fields. In particular, check Advanced > Feedback's Strength/Auto-stop, Advanced > Input's mouse percentages/Anti Radius, and the stick rotation/Fuzz/Anti Snapback units. Existing larger gaps are retained. The Settings latency unit was included in the same audit.
- **Nintendo Options > Feel game feedback:** check the Body rumble strength percent sign and Game rumble delay ms unit against their numeric fields. Their existing horizontal gaps are retained; their bottom spacing now matches the fields.
- **Smoothing wording:** “1€” is the formal [One Euro filter](https://gery.casiez.net/1euro/) name, not a monetary unit. Gyro selectors now say One Euro filter, and Profile Editor parameter labels say Min cutoff and Beta with explanatory tooltips. Settings' UDP smoothing parameters spell out One Euro. Check both smoothing parameters are separated from one another as well as aligned with their fields.

Settings bindings, field widths, values and mapping behaviour are unchanged. Existing vertical spacing on numeric controls is retained; labels, suffixes and inline checkboxes are centred relative to that spacing rather than to the whole container. The Outer-stick Invert options were moved into separate grid rows; no settings were removed.
