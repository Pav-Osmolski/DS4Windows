# Multi Action controls and editor follow-up

Related report: [#100](https://github.com/Pav-Osmolski/DS4Windows/issues/100#issuecomment-6551151016).

Automated checks reproduce the original `2, 3, 1` sequence and verify exclusive
single/double/triple recognition for matching separate actions. Different chords
or timing remain independent. Recognition completes on the input owner before
legacy key-action awaits; controller/profile resets retain their existing epoch
handling. No haptics/audio path is changed.

Persistence checks cover old action strings, enabled masks, Unicode names,
disabled recordings, re-enabling, and Actions.xml export/import. The first six
fields remain suitable for legacy readers, with disabled playback fields empty.

Layout checks render the real editor at 720 and 1100 logical pixels across
Light, Dark, Graphite, Paper, Midnight and High Contrast. They exercise action-type
selection, bounded scrolling, numeric heights, trigger availability and footer
geometry. Inline Rename tests switch modes on the same prompt, exercise name
collisions, preserve source bytes and confirm Duplicate still creates a copy.

## Maintainer acceptance

1. Open Edit Action for each action type. Check scrolling at the smallest usable
   window size and at the Windows scaling level you normally use.
2. In one Multi Action, record keys 1, 2 and 3 for tap, double and triple. Name
   each macro, save, reopen, then verify each gesture produces only its key.
3. Disable a recorded gesture, save/reopen and confirm its recording/name remain.
   Re-enable it and test playback. With triple disabled, double tap is immediate;
   a third tap can therefore begin a new gesture.
4. Repeat using separate enabled Multi Actions on the same button and identical
   hold/tap timing. Triple tap should emit only 3. Deliberately different timing
   is independent, so it is not covered by shared recognition.
5. Open Duplicate, then Rename, then Duplicate. Only one inline prompt should be
   visible. Rename should prefill the current name. Cancel and Escape close the
   prompt; selecting another profile closes it rather than changing its target.
6. Rename a disposable profile and verify active controller selection/tray names
   refresh. Check that collisions are reported inline and preserve both files.

Use copies of profiles/actions for testing. No release or automatic replacement
of the maintainer's running application is part of this change.
