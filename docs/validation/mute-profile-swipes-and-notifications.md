# Mute-button profile switching: swipe continuity and notifications

## Reproduction and acceptance

The maintainer reproduced this with a Bluetooth DualSense and two profiles,
Test Red and Test Blue. Both have profile-owned solid lightbar colours, input/output
muting disabled, mute-button profile switching enabled, Blue as mute-on and Red as
mute-off. Both profiles are included in the enabled two-finger swipe list.

Before the correction, swiping worked until the mute button loaded a temporary
profile. The gesture producer and UI consumer both rejected every temporary
profile, including this manual selection. The lightbar changed correctly when
the mute button switched profiles. With profile-switch notifications enabled and
the main window minimised, mute switching produced no notification.

Expected acceptance in the local preview:

1. Select Red normally and swipe between Red and Blue.
2. Press the physical microphone/mute button, then swipe again. Repeat both
   directions and several mute-button cycles. The selected profile and lightbar
   should agree, with one switch per gesture/press.
3. Enable profile-switch notifications, allow all application notifications and
   minimise DS4Windows. Successful mute-button and swipe switches should request
   the same profile notification. Windows notification suppression can still
   prevent display; the application retains its existing foreground-window policy.
4. Disable profile-switch notifications: switching and the log still work, with
   no profile notification requested.
5. While an Auto Profiles rule owns a temporary profile, swipes remain blocked,
   including when its target has the same name as a preceding mute selection.
   A mute selection must not acquire swipe authority from an automatic/held
   temporary profile. Missing profiles must not announce a successful switch.

## Implementation boundary

Successful mute-button loads explicitly grant temporary-profile swipe permission
at publication. Other temporary loads default to denying it; successful Auto
Profile loads revoke it. Matching rules also revoke permission when their target
is already loaded. A failed destination preserves the previous selection's
permission. Both gesture production and consumption consult the same policy.

Mute-button completion reports the applied profile using the existing translated
message and notification setting. Manual regular selections use the same feedback
helper. Failed/superseded worker requests do not announce an applied selection.
No speaker, microphone, haptics or lightbar output protocol is changed.

Regression coverage exercises the real serialized temporary/regular profile
worker, failed preparation, same-name automatic takeover, refusal to take authority
back from an automatic profile, and the existing registration/admission fences.
Notification tests observe application events without displaying a Windows toast.
Synthetic tests do not substitute for the physical acceptance sequence above.

## Validation result, 29 October 2026

The focused set passed 134 tests. The full x64 Release application suite passed
7,953 tests with 16 opt-in hardware checks skipped. The maintainer reproduced both
original failures on r5; physical acceptance of the correction is pending in r6.
The portable r6 ZIP passed CRC checks, preserves all 24 app language satellites,
contains the expected version and new policy/feedback types, and contains no
profiles or logs. SHA-256:
`ed526e80fa7f2b6e9cf3c7de8c7d995ab423644ccafb9cdca061e28e931d8d2d`.
