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

Nintendo Options uses different, already-balanced field rows and did not need changes for this alignment issue. Headings, captions above fields, and assignment summaries above buttons retain their existing spacing.

These edits change label margins and local checkbox alignment only. Settings bindings, field widths, values and mapping behaviour are unchanged. Existing vertical spacing on numeric controls is retained; grid-row labels are centred relative to that spacing rather than to the whole grid cell.
