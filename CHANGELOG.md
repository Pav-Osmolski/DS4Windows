# Changelog

Release history for the Pav-Osmolski DS4Windows fork. Downloads, source
archives, checksums, and build records are available on [Releases](https://github.com/Pav-Osmolski/DS4Windows/releases).

## Unreleased

- Make **Settings & profiles** expand/collapse immediately. Debounce and coalesce remembered-state writes off the UI thread, skip writes when the final choice matches the saved state, and keep presentation-state persistence separate from location/migration and update-retention preferences. Retain older saved choices and finish pending writes with a bound during application shutdown.

- Add remembered **Tiles**, **List** and **Details** profile views, keeping Tiles as the default and preserving selection, search, sorting and profile actions across view changes. Details shows the emulated controller and last-modified time.
- Use each profile name's first Unicode character for its badge. Add **Profile icon…** to choose a fill colour or a transparent ICO/PNG image, preview changes, and restore the default. Embed bounded PNG thumbnails in the profile XML so appearance survives normal saves, rename, duplicate, import/export, backups and migration. Include the new interface text in all 24 language packs. ([PR #86](https://github.com/Pav-Osmolski/DS4Windows/pull/86))

## Released

### [5.0.52](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.52) - 2026-10-26

#### Installer and uninstaller

- Include HidHide and FakerInput in the installation-status list. Select HidHide by default only when it is missing, keep FakerInput opt-in, retain choices across re-detection, and keep existing shared drivers unchanged during application installation and repair. Detect HidHide through its driver service across product versions and recognise related FakerInput MSI versions, including newer installations.
- Show the DS4Windows version being installed and the detected installed HidHide/FakerInput versions. Missing drivers show the bundled version; unreadable installed metadata is reported as unavailable rather than guessed. Derive the VIIPER label from the verified version constant instead of a stale label.
- Give uninstall its own component summary: remove DS4Windows and its managed VIIPER copy while preserving USB/IP, HidHide, FakerInput, profiles and settings. Hide installation-only options and instructions during uninstall.
- Add **Open profiles folder** and **Manage shared drivers…** shortcuts to the uninstall confirmation. Offer both existing AppData and program-folder profile locations when present; open Windows Installed apps for separately managing shared drivers. These controls do not change the existing removal policy.
- Implemented in [PR #85](https://github.com/Pav-Osmolski/DS4Windows/pull/85).

### [5.0.51](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.51) - 2026-10-26

#### Update storage

- Add update storage management under **Settings > Updates & tools > Updates**: retain one previous version per component by default, choose none/a custom number/all, see stored and reclaimable space, preview manual cleanup, and inspect stored files. Automatically prune recognised older packages after a successful startup with healthy VIIPER/USB/IP prerequisites. Thanks to [@Jobima1st](https://github.com/Jobima1st) for reporting accumulated update files in [issue #83](https://github.com/Pav-Osmolski/DS4Windows/issues/83).
- Keep current/future packages, current package-owned files, profiles, settings, backups, unknown files and nested content. Recheck policy and ownership before cleanup, and skip files that are in use, changed or inaccessible. Unknown updater versions are preserved rather than guessed.

#### Interface improvements

- Collapse **Settings & profiles** by default, show a compact location/access summary, and remember the user's expanded state across restarts. Automatically expand read-only folders and pending settings operations so their recovery controls remain visible.
- Use a standard circular expander arrow with native hover/pressed feedback and a stationary, vertically centred position. Keep the full rounded header highlighted while hovering the arrow, with a separate keyboard-focus outline.
- Match update storage and Nintendo discovery dividers to the theme's outline colour, remove empty status spacing, and balance the Nintendo divider's margins. Include all six themes and all 24 language packs. Implemented in [PR #84](https://github.com/Pav-Osmolski/DS4Windows/pull/84).

### [5.0.50](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.50) - 2026-10-25

#### Settings and profile management

- Add **Settings & profiles** controls to the Profiles page: choose AppData or the current program folder, see the active path and actual write-access status, open the profiles folder, and back up or restore the complete settings set. New installed copies default to AppData; existing choices remain optional. Thanks to [@1hezio](https://github.com/1hezio) for reporting the Program Files save error in [issue #80](https://github.com/Pav-Osmolski/DS4Windows/issues/80).
- Offer optional **Migrate to AppData** for installed copies using their program folder. Stage and verify copies before applying them on the next start, keep the source files, require explicit confirmation before replacing an existing destination, and preserve a verified destination backup. A recovery journal supports interrupted copies and refuses to overwrite unexpected competing edits. Settings sets are not silently merged.
- Remember the selected location independently of legacy files beside the EXE. Remove the old chooser's deletion of the other settings folder and its unreliable copy-complete message. Clearly block profile writes when the selected folder is read-only.
- Show profile output type, modified time and in-use status; add name/modified sorting and a search hint; confirm import replacements. Make the full page scroll at smaller window sizes. Include the new controls in all six themes and all 24 language packs. Implemented in [PR #82](https://github.com/Pav-Osmolski/DS4Windows/pull/82).

#### DualShock 4 Bluetooth headset microphone

- Pair VIIPER **0.1.17** to correct the rejected microphone-status request exposed by the reporter's logs. DS4Windows can now detect when Windows opens the virtual DualShock 4 microphone and arm the physical Bluetooth microphone route, including the audio companion used with Xbox output. Thanks again to [@1hezio](https://github.com/1hezio) for the follow-up logs in [issue #80](https://github.com/Pav-Osmolski/DS4Windows/issues/80).
- Cover all three DS4 registration aliases, exact-device selection, capture-state transitions, buffered PCM delivery and status reads that do not drain the audio buffer. Include the matching backend source, notices, hashes and build provenance.

### [5.0.49](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.49) - 2026-10-25

#### Controller information

- Add **Controller info** buttons to Overview and each Controllers card. A shared window separates the connected physical controller from the emulated controller, with selectable values, manual Refresh and Copy info.
- Show available USB identity, battery/status and factory/firmware fields for all six emulated output types: DualShock 4, DualSense, DualSense Edge, Xbox 360, Xbox One and Switch 2 Pro. Unsupported fields remain unavailable; emulated firmware dates are clearly distinguished from physical hardware and DS4Windows versions. Switch 2 battery bands retain their reported precision.
- Pair VIIPER **0.1.16** with a read-only information endpoint and regression coverage across the actual controller implementations. Correctly treat USB string index zero as language information rather than a serial number. Backend: [VIIPER PR #11](https://github.com/Pav-Osmolski/VIIPER/pull/11).

#### Interface refinements

- Match the selected controller card's left and right margins in light and dark themes.
- Use rounded, themed information tabs with readable selected/unselected labels across all six app themes. Tighten the information rows, remove the unnecessary bottom rule, and avoid the automatic blue focus outline on Controller info while retaining keyboard focus feedback.
- Include the new interface strings in all 24 supported language packs.

### [5.0.48](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.48) - 2026-10-24

#### Controller factory information

- Correct the emulated DualShock 4 factory build date/time with the paired VIIPER **0.1.15** backend. The time occupied the final byte of the date field, so tools could display `Sep 17 20211 1:34:00` instead of `Sep 17 2021 11:34:00`. The original emulated build date is retained; it describes controller firmware, not the DS4Windows application. Thanks again to [@Jobima1st](https://github.com/Jobima1st) for the follow-up in [issue #75](https://github.com/Pav-Osmolski/DS4Windows/issues/75).
- Audit the other emulated controller metadata formats and add independent host-decoding regression coverage for DualShock 4, DualSense and DualSense Edge. Default/custom dates, date/time padding, version-field boundaries and short reads are covered; no corresponding date/time overlap was found in the other output types. Battery/charging propagation and the 5.0.47 connection reliability fixes are retained. Backend implementation: [VIIPER PR #10](https://github.com/Pav-Osmolski/VIIPER/pull/10).

#### Updating

- Fully exit DS4Windows and VIIPER before updating to replace the paired backend. USB/IP remains **0.9.8.1**; no driver version change is required. Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.47](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.47) - 2026-10-24

#### Important fixes: controller startup and installer readiness

- Resolve the reported intermittent virtual-controller startup failure with the paired VIIPER **0.1.14** backend: finish complete management replies gracefully before closing their connections. Keep cleanup bounded and interruptible by shutdown, and preserve controller/audio streaming, authentication and the existing single stream-open retry. Thanks to [@cns00](https://github.com/cns00) for the diagnostic captures and final successful testing in [issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66), also reported in [upstream #153](https://github.com/hbashton/DS4Windows/issues/153). On 24 October the reporter confirmed consistently reaching **Ready** in the final paired preview, and the maintainer approved this release. The earlier isolated comparison improved from 77/400 failed requests to 3/400 with graceful closing; those measurements describe the diagnostic session, not a guarantee against every future reset. No hardware fault or machine-specific underlying cause has been established.
- Correct a separate fast-completion race in both the application's readiness probe and the installer's final health-check probe, replacing legacy shared connection wait handles with cancellable deadlines. Add 256 rapid loopback checks for each real probe. Thanks again to [@cns00](https://github.com/cns00) for the installer follow-up: setup helpers succeeded but a separate final check failed, and Retry succeeded. The supplied installer diagnostics do not distinguish that race from a transient reset. Driver, version, hash and health validation remain enforced.
- Retain all stable 5.0.46 features and translations. Temporary diagnostic collectors, independent testers and experimental server switches are excluded. Implementation: [DS4Windows PR #77](https://github.com/Pav-Osmolski/DS4Windows/pull/77) and [VIIPER PR #9](https://github.com/Pav-Osmolski/VIIPER/pull/9).

#### Updating

- Close games, DS4Windows and VIIPER before updating so the matching backend can be replaced. USB/IP remains **0.9.8.1**; no driver version change is required. Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.46](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.46) - 2026-10-23

#### Controller audio

- Add **Audio output > Follow headphone jack** in Overview and **Profile Editor > Advanced > Audio**. Automatically switch to headphones when connected and return to the controller speaker when unplugged. Keep manual speaker/headset choices and existing profiles unchanged. Supports managed DualSense/Edge USB and Bluetooth audio and genuine DualShock 4 Bluetooth audio; Windows manages DualShock 4 USB audio. An active Audio Haptics app-audio override keeps its own routing choice. Thanks to [@Jobima1st](https://github.com/Jobima1st) for [issue #74](https://github.com/Pav-Osmolski/DS4Windows/issues/74) and [upstream #79](https://github.com/hbashton/DS4Windows/issues/79).
- Translate the new routing choice in all 24 language packs and preserve it when saving/reopening profiles.

#### Virtual battery status

- Forward reliable physical battery and charging state to virtual DualShock 4, DualSense/Edge and Switch 2 Pro through the paired VIIPER **0.1.13** status API. Preserve native DualSense status passthrough and Switch 2 measured-voltage reporting. Values follow each virtual protocol's battery bands; Xbox outputs remain wired USB controllers. Thanks to [@Jobima1st](https://github.com/Jobima1st) for [issue #75](https://github.com/Pav-Osmolski/DS4Windows/issues/75) and [upstream #111](https://github.com/hbashton/DS4Windows/issues/111).
- Clarify that a browser tester's factory build date describes emulated controller firmware, not the DS4Windows application build date.
- Include VIIPER's reviewed management-reply completion and split UTF-8 fixes. These preserve the server wire format and do not establish a root cause for the reporter's intermittent connection resets. Thanks to [@cns00](https://github.com/cns00) for the diagnostic reports in [issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66) and [upstream #153](https://github.com/hbashton/DS4Windows/issues/153).

#### Updating

- Close games, DS4Windows and VIIPER before updating so the paired backend can be replaced. USB/IP remains **0.9.8.1**; no driver version change is required. The maintainer successfully tested the paired preview. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.45](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.45) - 2026-10-23

#### Appearance and languages

- Correct the executable's company, authors, description and project/repository links for the Pav-Osmolski fork, retaining the original copyright credits.
- Add Graphite, Paper, Midnight and High contrast app themes, palette previews, preset accents and a custom accent picker. Follow Windows replaces the ambiguous Default label; Windows contrast settings take priority. Theme and accent choices apply immediately and persist across restarts.
- Complete both language resource sets for all 24 supported languages and make newer interface screens translatable. Preserve existing translations where suitable, fill gaps with offline machine-assisted translations, repair placeholder errors and update obsolete driver/setup guidance. Correct Swedish and Indonesian culture identifiers with migration of saved choices; consistently capitalize native language names. Resource coverage and formatting are checked automatically; wording remains open to native-speaker improvements.
#### Build and maintenance

- Avoid duplicate push/PR tests, retain full tests on WIP updates, and offer explicit portable/installer previews. Packaging and infrastructure PRs keep pre-merge installer validation; known documentation-only changes use text/site checks.
- Build final-version release candidates once on main. Release preparation verifies the exact successful main CI run, artifact and package hashes, then promotes those same portable/installer bytes. Keep an explicit fully validated rebuild fallback, schema-1 updater compatibility, signing policy and no-overwrite protection.
- Use cached Python 3.12 for packaging and avoid compressing package archives again during artifact upload. Clarify the legacy RC4.5.5 upgrade-test name and also verify upgrades from pinned numeric stable 5.0.43, preserving profiles across repair/uninstall.
- Document issue classifications, distinct closure outcomes and genuine dependencies separately from general cross-references.

Themes, language coverage and fork metadata are tracked in [PR #73](https://github.com/Pav-Osmolski/DS4Windows/pull/73).

The build and release improvements are tracked in [PR #72](https://github.com/Pav-Osmolski/DS4Windows/pull/72).

### [5.0.44](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.44) - 2026-10-22

#### Important fix: connected controller, but no input in games

- A controller without an active profile could remain at **Connected**, with no virtual controller created and no explanation of what was missing. Overview now shows **Select a profile**, explains how to choose **Controllers > Active Profile**, and provides a **Select profile** button. If the list is empty, create a profile under **Profiles** first. Thanks to [@cns00](https://github.com/cns00) for the follow-up logs in [issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66). The maintainer reproduced this condition and verified the recovery in the preview.
- Disable Overview's quick profile controls and profile editing actions until a profile is selected, and show the emulated device as **Not configured**. This avoids presenting settings as saved when there is no selected profile. Check empty-selection recovery, settings persistence, and the guidance in light and dark themes.

#### Startup diagnostics

- Record the initial startup prerequisite result and elapsed time before any retry, recovery or repair prompt, so a later successful check does not hide the earlier failure.
- Retry a verified USB/IP runtime probe once after 500 ms when it explicitly times out. Unsupported versions, integrity failures, ABI errors and process conflicts retain their existing checks. This handles a possible transient startup delay; it does not treat every repair warning as a timeout.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.43](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.43) - 2026-10-22

#### Added

- Capture VIIPER's own debug log from the Log page without manually launching the backend. Keep managed authentication and process checks, preserve the preceding capture, and apply changes on the next backend start without interrupting the current controller session. Includes instructions for collecting both application and backend logs when investigating [issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66), with thanks to [@cns00](https://github.com/cns00) for the diagnostic reports.

#### Fixed

- Finish reading VIIPER management replies at their newline terminator instead of waiting for the connection to close. A connection reset after a complete reply could otherwise discard a successful response and trigger virtual-controller cleanup. Preserve response validation and failures for incomplete or damaged encrypted replies. Thanks to [@cns00](https://github.com/cns00) for the reports and diagnostic logs. ([Issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66), [upstream issue #153](https://github.com/hbashton/DS4Windows/issues/153))
- Add regression coverage for completed replies followed by connection resets, fragmented encrypted records, incomplete or invalid responses, and TCP connections that stay open after sending a reply. The maintainer tested the preview successfully; [issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66) remains open pending confirmation on the reporter's computer.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.41](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.41) - 2026-10-21

#### Added

- Optional repeating low-battery warnings: every 30 seconds, 1, 2, 5 or 10 minutes, with a total of 1–10 warnings (including the first) or until charging. Repeating defaults to Off. Cancel the sequence on charging, recovery, disconnect, invalid readings or disabled alerts, and discard queued warnings from ended sequences. Thanks to [@Masterload1975](https://github.com/Masterload1975) for the suggestion. ([Issue #45](https://github.com/Pav-Osmolski/DS4Windows/issues/45))

#### Fixed

- Publish release documentation through a main-branch workflow run so GitHub Pages deployment respects the existing main-only environment rule.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.40](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.40) - 2026-10-21

#### Added

- Optional battery alerts in Settings > Controllers: installed Windows voices, selected audio output, separate low/full/on-demand sounds and vibration patterns, previews and cancellation. Import WAV, MP3 or AIFF into the settings folder. Extend Check Battery Special Actions without changing existing actions. Require stable valid readings, suppress repeats across reconnects/profile changes, and use explicit DS3/DS4/DualSense charging-completion status for full-charge notifications. Controller-speaker alerts reuse an already enabled matching native audio route. Thanks to [@Masterload1975](https://github.com/Masterload1975) for the suggestion. ([Issue #45](https://github.com/Pav-Osmolski/DS4Windows/issues/45))

#### Fixed

- Trigger low-battery alerts from the reported low band on DS3 and original Nintendo controllers, instead of comparing their approximate 25% value with the default 20% threshold. Keep percentage thresholds for controllers that provide percentage readings.
- Update Overview slider numbers immediately while dragging, retaining the delay for settings and hardware updates. Add transparent icon previews to Settings > Icon Choice.
- Preserve Profile Editor audio dropdown selections across endpoint refreshes and reopening, including automatic and unavailable saved devices. Send USB DualSense battery announcements directly to the matching physical speaker, keeping haptics channels silent; report muted/zero-volume outputs clearly. Use the USB internal-speaker routing value in both local and native-game reports while preserving Bluetooth audio routing.
- Match USB DualSense speaker auto-detection to the physical controller and keep its game-audio capture source separate from the speaker destination. Prefer the matching VIIPER USB/IP port and restart capture when that port changes. Correct battery-alert guidance to Profile Editor > Advanced > Audio.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.39](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.39) - 2026-10-21

#### Fixed

The Auto Profiles and Hotkeys/About refinements below are part of [PR #62](https://github.com/Pav-Osmolski/DS4Windows/pull/62).

- Prevent Hotkeys/About from focusing a link at the bottom when changing tabs. Give each page's scroll container an initial keyboard stop and reset its position before rendering, replacing the delayed correction that caused a visible jump.
- Rework Auto Profiles discovery with visible progress, cancellation, duplicate handling and completion/error messages. Load executable icons for displayed rows in the background, with cached results and a fallback icon. Resolve Start Menu shortcut targets on a dedicated background apartment without Windows Installer probes.
- Separate saving, enabling/disabling and deleting Auto Profile rules. Delete removes saved and unsaved rows; Rule enabled preserves a rule while pausing it; edits apply on Save rule. Display rule priority instead of alphabetical sorting, make moves work for unsaved rows too, and select the right-clicked row before showing move actions.
- Align Auto Profiles toolbar button heights and give the rule editor consistent scrollbar clearance in both themes.
- Centre the Auto Profiles sidebar divider between the title field and global options, using a one-logical-pixel line (two physical pixels at 200% scaling). Preserve all controller assignments and write Auto Profiles configuration atomically.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.38](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.38) - 2026-10-20

#### Improved

- Redesign Hotkeys/About with an artwork-led About page, grouped shortcut explanations, consistent theme-aware cards and tabs, and accessible credits/component links. Adapt the maintainer-provided promotional artwork into a decorative banner with sharp live text. Keep the complete GPL licence embedded directly from `COPYING`, add a persistent Close button/Escape action, and fit the dialog to the owner's scaled monitor work area. ([PR #61](https://github.com/Pav-Osmolski/DS4Windows/pull/61))

#### Fixed

- Keep About link-button hover, pressed and focus states readable in both themes. Open each selected help page at the top after tab focus restoration.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.37](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.37) - 2026-10-20

#### Fixed

- Make the observed physical report interval in Controller Readings easier to read: show exactly two decimal places in each supplied translation and refresh the number every 250 ms (four times per second). Refresh immediately when switching controllers or reopening the readings page. Controller sampling, live plots, warning detection and internal measurement precision retain their existing behaviour. Thanks to [@Basfet](https://github.com/Basfet) for reporting this ([PR #60](https://github.com/Pav-Osmolski/DS4Windows/pull/60), [issue #59](https://github.com/Pav-Osmolski/DS4Windows/issues/59)).

#### Documentation

- Give the website homepage a complete light palette while preserving its dark presentation. Refine migration guidance and record the maintainer's and original reporter's confirmation of the released DualSense USB/Bluetooth haptics fix in troubleshooting and the historical validation record.

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.12** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.36](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.36) - 2026-10-20

#### Added

- Launch the [DS4Windows website](https://pav-osmolski.github.io/DS4Windows/) with searchable setup and troubleshooting guides, light/dark reading themes and refreshed screenshots, including Special Actions and Axis Config ([PR #57](https://github.com/Pav-Osmolski/DS4Windows/pull/57)).
- Adjust the sidebar icon's hue in **Settings > Appearance > Sidebar icon**. The icon and dropdown previews update while dragging; the hue is saved, and **Reset** restores the original artwork. Transparency and neutral colours are preserved ([PR #58](https://github.com/Pav-Osmolski/DS4Windows/pull/58)).

#### Fixed

- Centre the button-mapping dialog's RGB sliders inside equal, aligned channel backgrounds in both themes. Remove the Add Programs popup's outer item gaps, follow the current light/dark theme, use a full-width separator in both themes and style its separator separately so opening the menu does not crash ([PR #58](https://github.com/Pav-Osmolski/DS4Windows/pull/58)).

- Keep the startup header and session in one active log when loading or changing retention. Avoid repeating startup rollover while preserving retention cleanup and daily/size rotation ([PR #58](https://github.com/Pav-Osmolski/DS4Windows/pull/58)).

#### Changed

- Replace the Angular Badge sidebar icon with the updated maintainer-supplied transparent artwork ([PR #58](https://github.com/Pav-Osmolski/DS4Windows/pull/58)).

### [5.0.35](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.35) - 2026-10-19

#### Added

- Choose the sidebar's icon in **Settings > Appearance > Sidebar icon**: **Blue Tile** (first and default), **Midnight Circle**, **Blue Frame**, **Controller** or **Angular Badge**. Changes apply immediately and the choice is saved. The transparent artwork uses a 44 × 44 device-independent-pixel layout for display scaling ([PR #56](https://github.com/Pav-Osmolski/DS4Windows/pull/56)).

#### Changed

- Move the application, installer hosts and diagnostic tools to **.NET 10 LTS**, pin SDK **10.0.402**, and refresh dependency locks. Packages include their runtime; no separate .NET installation or controller-driver update is required. Add the running runtime version to saved and copied startup logs and remove unused .NET 8 installation notices ([PR #56](https://github.com/Pav-Osmolski/DS4Windows/pull/56)). See the [migration validation and Windows compatibility notes](docs/validation/2026-10-19-net10-migration.md).

### [5.0.34](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.34) - 2026-10-19

#### Fixed

- Improve DualSense game feedback over USB and Bluetooth: pace native output commands in VIIPER using its advertised HID output interval, reduce redundant consecutive pending state updates in DS4Windows, and preserve the game's Bluetooth rumble mode across trigger-only updates. Distinct effects, final stops and command ordering remain intact. Maintainer testing confirmed normal gameplay in DEATHLOOP and GTA V Enhanced over both connections, including extended Bluetooth play. On 2026-10-19, [the original reporter also confirmed that the haptic problems were fixed over USB and Bluetooth](https://github.com/Pav-Osmolski/DS4Windows/issues/47#issuecomment-6274412583). Thanks to [@Jobima1st](https://github.com/Jobima1st) for the reports and logs ([DS4Windows PR #54](https://github.com/Pav-Osmolski/DS4Windows/pull/54), [VIIPER PR #6](https://github.com/Pav-Osmolski/VIIPER/pull/6), [issue #47](https://github.com/Pav-Osmolski/DS4Windows/issues/47), [upstream issue #119](https://github.com/hbashton/DS4Windows/issues/119)).
- Overhaul the in-app changelog and update-notes reader: clearer release spacing, theme-aware tables, readable links/code/quotes, images constrained to the window, and loading/error/retry states. Add a GitHub releases shortcut and improve window sizing and footer layout ([PR #53](https://github.com/Pav-Osmolski/DS4Windows/pull/53)).
- Refine Profile Editor > Nintendo Options > Feel game feedback: centre % and ms with a consistent 3px gap, keep fields and units together when wrapping, and align checkbox groups and numeric fields ([PR #53](https://github.com/Pav-Osmolski/DS4Windows/pull/53)).

#### Added

- Add capability-aware **Controller polling** information. New DS4-compatible profiles use Automatic Bluetooth input rate, with an optional manual override; existing profiles keep their chosen rate. Automatic uses the conservative 250 Hz (4 ms) DS4 request without probing unsupported rates. DualSense/Edge and Nintendo retain their managed timing. Unverified DS4-compatible identities are identified, and device IDs are not presented as proof of genuine hardware or an accepted rate ([PR #54](https://github.com/Pav-Osmolski/DS4Windows/pull/54)).
- Detect advertised physical USB HID input/output intervals separately using the selected interface, negotiated speed and active pipes. Exclude audio endpoints and show unknown when detection is unavailable. This does not overclock USB or alter VIIPER's separate feedback pacing ([PR #54](https://github.com/Pav-Osmolski/DS4Windows/pull/54)).

#### Changed

- Bundle verified VIIPER **0.1.12**, with its matching source, complete notices and build records. USB/IP remains **0.9.8.1**; no driver version change is required ([VIIPER PR #6](https://github.com/Pav-Osmolski/VIIPER/pull/6), [DS4Windows PR #54](https://github.com/Pav-Osmolski/DS4Windows/pull/54)).
- Update the pinned .NET SDK to **8.0.426**, refresh installer dependency locks and update development documentation ([PR #55](https://github.com/Pav-Osmolski/DS4Windows/pull/55)).

Close games, DS4Windows and VIIPER before updating, and allow the bundled backend update. These Windows packages are unsigned; matching source archives, checksums and build records accompany the downloads.

### [5.0.33](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.33) - 2026-10-18

#### Added

- Add opt-in **Advanced tuning** to Audio Haptics with adjustable low-pass (40-1400 Hz) and high-pass (0-200 Hz; 0 disables high-pass) filters. Save the controls per profile and use the existing intensity adjustment. Custom filters replace Bass Focus only when enabled; older profiles retain their previous response ([#52](https://github.com/Pav-Osmolski/DS4Windows/pull/52)). Thanks to [u/d3boshy](https://www.reddit.com/user/d3boshy/) for the request, with [dsz](https://github.com/MaximumFreak/dsz) as the reference for the controls.
- Make appreciation for [Nefarius](https://github.com/nefarius) and links to [support his projects](https://docs.nefarius.at/Donations/) more visible in the README, contributors page and About > Credits. Explain ViGEm's role in earlier DS4Windows versions, HidHide's ongoing contribution and this fork's current VIIPER backend ([#51](https://github.com/Pav-Osmolski/DS4Windows/pull/51)).

#### Fixed

- Align the description and first card across all six Settings categories. Avoid fractional layout-rounding gaps from collapsed grid rows, while preserving multi-section search results and unsaved edits ([#52](https://github.com/Pav-Osmolski/DS4Windows/pull/52)).

#### Using the new controls

- Open **Audio Haptics > Response > Advanced tuning** and enable **Use custom frequency filters**. Start around 100-160 Hz low-pass and 20 Hz high-pass, then adjust intensity gradually. Filters can reduce vibration from dialogue but cannot fully separate voices from other sounds. They do not filter audible audio or game-authored native haptics. See the [Audio Haptics guide](docs/user-guide.md#audio-haptics).
- Close games, DS4Windows and VIIPER before updating. VIIPER remains at **0.1.11** and USB/IP at **0.9.8.1**; no driver version change is required. These Windows packages are unsigned; matching source, checksums and build records accompany the release.

### [5.0.32](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.32) - 2026-10-18

#### Improved

- Refresh the Log page with **Verbose logging**, **Open log folder**, **Report an issue**, and separate **Clear visible list** / confirmed **Clear stored logs** actions ([#50](https://github.com/Pav-Osmolski/DS4Windows/pull/50)). Reporting opens GitHub without uploading anything automatically.
- Add saved-log retention choices of 1, 7, 14, 30 or 90 days, defaulting to 30 days. Rotate archives daily or at 10 MB, retaining at most 20 archives. Keep the active session log, profiles and unrelated files when clearing archives ([#50](https://github.com/Pav-Osmolski/DS4Windows/pull/50)).
- Include physical DualSense native-feedback queue depth, age, owner duration, retries, report flags and explicit motor-stop receipt diagnostics in the copyable verbose log, including during speaker streaming. Distinguish physical USB write completion from Bluetooth transport handoff ([#50](https://github.com/Pav-Osmolski/DS4Windows/pull/50), [#47](https://github.com/Pav-Osmolski/DS4Windows/issues/47)).

#### Fixed

- Suppress consecutive identical, unclaimed, recognized USB motor/trigger state reports that include actuator-power settings. Preserve distinct commands, stops, media, ownership boundaries and Bluetooth behaviour ([#50](https://github.com/Pav-Osmolski/DS4Windows/pull/50), [#47](https://github.com/Pav-Osmolski/DS4Windows/issues/47)).

#### Validation and updating

- Thanks to [@Jobima1st](https://github.com/Jobima1st) for the logs and report in [#47](https://github.com/Pav-Osmolski/DS4Windows/issues/47), related to upstream [#119](https://github.com/hbashton/DS4Windows/issues/119). The reported game delay and Bluetooth strength/skipping still need hardware confirmation; this release does not claim a complete fix. See the [validation record](docs/validation/2026-10-18-native-feedback-and-logs.md) and [Log guide](docs/user-guide.md#log-and-diagnostics).
- Close games, DS4Windows and VIIPER before updating. VIIPER remains at 0.1.11 and USB/IP at 0.9.8.1; no driver version change is required. This release is unsigned.

### [5.0.31](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.31) - 2026-10-18

#### Fixed

- Let an unassigned, unlinked Switch 2 input slot use the existing Default profile so Joy-Con activation does not fail solely because that slot has no saved selection. Explicit custom selections are preserved ([#49](https://github.com/Pav-Osmolski/DS4Windows/pull/49), [#48](https://github.com/Pav-Osmolski/DS4Windows/issues/48)).
- Validate activation profiles before resetting mappings. Report the input slot, selected profile and missing/invalid/unreadable load failures separately from unavailable virtual output; improve the reconnect guidance ([#49](https://github.com/Pav-Osmolski/DS4Windows/pull/49)).

#### Validation and updating

- Thanks to @doyoulikegirlz for reporting [#48](https://github.com/Pav-Osmolski/DS4Windows/issues/48). Automated production-stage tests reproduce profile failures and cover the changes. In a [later follow-up](https://github.com/Pav-Osmolski/DS4Windows/issues/48#issuecomment-6252913392), the reporter attributed their slot-assignment problem to merged very old backups and confirmed a clean install resolved it. The exact conflicting configuration was not recovered; [PR #49](https://github.com/Pav-Osmolski/DS4Windows/pull/49)'s independently tested profile-loading safeguards remain useful. See the [validation record](docs/validation/2026-10-18-switch2-profile-activation.md) and [troubleshooting guide](docs/troubleshooting.md#switch-2-controller-is-reached-but-its-slot-cannot-activate).
- Close games, DS4Windows and VIIPER before updating. VIIPER remains at 0.1.11 and USB/IP at 0.9.8.1; no driver version change is required. This release is unsigned.

### [5.0.30](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.30) - 2026-10-17

#### Fixed

- Bound stale DualSense Bluetooth haptics backlog and pending-write recovery ([#42](https://github.com/Pav-Osmolski/DS4Windows/pull/42)).
- Restore Profile Editor Heavy/Light motor previews over USB after native DualSense output, retaining the game's motor state when the preview ends ([#42](https://github.com/Pav-Osmolski/DS4Windows/pull/42)).
- Forward native DualSense/Edge waveform haptics from the matching virtual four-channel Windows audio endpoint to the physical USB controller, independently of speaker streaming and Audio Haptics. Resolve indexed Windows audio-device identities when matching physical endpoints ([#42](https://github.com/Pav-Osmolski/DS4Windows/pull/42)).
- Match gyro activation field/dropdown heights to the other Profile Editor controls ([#42](https://github.com/Pav-Osmolski/DS4Windows/pull/42)).

#### Validation and updating

- The maintainer confirmed USB Heavy/Light editor tests, DEATHLOOP intro waveform haptics over USB and Bluetooth, and Bluetooth controller-speaker playback. The original upstream Bluetooth stall was not reproduced on this machine; the backlog changes provide recovery safeguards without claiming that hardware-specific report is conclusively resolved ([#42](https://github.com/Pav-Osmolski/DS4Windows/pull/42)).
- Close games, DS4Windows and VIIPER before updating. VIIPER remains at 0.1.11 and USB/IP at 0.9.8.1; no driver version change is required. This release is unsigned.

### [5.0.29](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.29) - 2026-10-17

#### Added

- Add **Stick-based gyro activation** in Profile Editor > Gyro. Enable or pause gyro while the left, right or either stick is held beyond an adjustable threshold. Applies to mapped aiming and supported virtual-controller gyro output; accelerometer/gravity and UDP/DSU motion remain active ([#43](https://github.com/Pav-Osmolski/DS4Windows/pull/43)).
- Add per-profile **Indicator LEDs** controls for DualSense and DualSense Edge in the Lightbar tab. Choose white player-light patterns, battery indication or Off, and orange microphone-light Off, On or Pulse. Device settings in the existing **Passthru** mode follows application requests and restores their latest remembered values after a profile override is removed ([#46](https://github.com/Pav-Osmolski/DS4Windows/pull/46), [#40](https://github.com/Pav-Osmolski/DS4Windows/issues/40)).
- Add **Reapply indicators** so lights left on by another application can be cleared without changing profiles, including when Off is already selected ([#46](https://github.com/Pav-Osmolski/DS4Windows/pull/46)).

#### Improved

- Replace the silent external **XInputChecker** launcher with a built-in controller test. Inspect all four Windows XInput slots, buttons, triggers and sticks without downloading another tool. XInput slot numbers are separate from DS4Windows output slots; virtual PlayStation outputs do not appear in this test ([#44](https://github.com/Pav-Osmolski/DS4Windows/pull/44)).
- Update the user guides for indicator ownership, stick-based gyro conditions and XInput diagnostics. Spell out One Euro smoothing-filter terminology in English ([#41](https://github.com/Pav-Osmolski/DS4Windows/pull/41)).

#### Fixed

- Remove the top/bottom padding from standard Dark-theme dropdown lists and use a #2054A0 popup border. Preserve the closed toggle border, Light theme and the separate rounded profile/controller dropdown styles ([#41](https://github.com/Pav-Osmolski/DS4Windows/pull/41)).
- Remove the right-hand gutter from the Controllers Edit > New Profile menu. Centre Profile Editor labels, inline checkboxes and unit suffixes beside their fields, with small readable gaps before units ([#41](https://github.com/Pav-Osmolski/DS4Windows/pull/41)).
- Forward native microphone-light requests in Passthru and restore application player/microphone indicators after Off without requiring a new game packet. Preserve microphone mute indication, DSX priority and unrelated feedback fields ([#46](https://github.com/Pav-Osmolski/DS4Windows/pull/46)).

#### Updating

- Close games, DS4Windows and VIIPER before updating. VIIPER remains at 0.1.11 and USB/IP at 0.9.8.1; no driver version change is required. This release is unsigned.
- The maintainer confirmed the UI, native gyro forwarding, XInput diagnostics and indicator behaviour during testing. The separate Bluetooth haptics backlog candidate ([#42](https://github.com/Pav-Osmolski/DS4Windows/pull/42)) is **not included** and remains under extended hardware testing.

### [5.0.28](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.28) - 2026-10-16

#### Improved

- Tidy Nintendo Switch 2 setup with left-aligned discovery results, wrapping controller names and pairing selectors, and consistent spacing around the join action.
- Reorganize Nintendo Options with clearer section headings, aligned button-choice tiles and three steps for assigning second actions. Refine spacing throughout in both themes and remove unused space below dynamic trigger-rumble settings.
- Give the Advanced DS4 Trigger Output Mode row more separation from the checkbox above it.
- Simplify release downloads to seven uploaded files by grouping licences, notices and build information into one release-information ZIP. Keep both matching source archives, checksums and the updater build record separately available.
- Refresh the README, organize inherited release history, and update notices and contributor credits.

#### Fixed

- Enforce the VIIPER readiness-probe deadline during synchronous I/O so an unresponsive probe cannot exceed its startup allowance.

### [5.0.27](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.27) - 2026-10-15

#### Fixed

- Keep Trigger Lab surfaces, labels and buttons consistent when switching between Dark and Light themes, including the main navigation page and Profile Editor. Light theme uses the shared pale blue raised surface (`#EAF1FA`).
- Restore readable Profiles and Auto Profiles toolbar buttons in Light theme, with profile toolbar icons following their button text colour.
- Give normal and low-battery RGB sliders distinct pale red, green and blue surfaces in Light theme, including zero-valued channels, while preserving Dark theme's existing channel colours.
- Preserve exact virtual-controller ownership when disconnect or feedback cleanup fails, so removal can be retried safely. Make duplicate removal safe and reject invalid slot indices.
- Commit slot changes before publishing notifications, deliver callbacks outside the manager lock in commit order, and isolate failing subscribers so remaining observers still run. Prevent an old disconnect from erasing a replacement controller binding.
- Continue shutting down healthy outputs when another output fails, retain failed registrations for retry, and reject reentrant attachment while shutdown is in progress. Builds on upstream work by @hbashton with additional lifecycle regression coverage.

### [5.0.26](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.26) - 2026-10-15

#### Fixed

- Fix enabled Special Actions failing to save when selecting existing actions for another profile ([#33](https://github.com/Pav-Osmolski/DS4Windows/issues/33)). Checkbox changes now apply immediately, and saving captures the current selection, including disabling every action or renaming an enabled action.

### [5.0.25](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.25) - 2026-10-15

#### Added

- Add **Show VIIPER tray icon** in Settings under Virtual controller support. It is enabled by default, saved between sessions, and respected by portable and installed startup. Changes apply the next time VIIPER starts, leaving active controller sessions running. DS4Windows retains its own separate tray icon.

#### Improved

- Bundle [VIIPER 0.1.11](https://github.com/Pav-Osmolski/VIIPER/releases/tag/v0.1.11), adapting reviewed upstream improvements to this fork's DS4Windows integration.
- Preserve unaffected rumble, RGB and flashing values when games send partial virtual DS4 output commands. Keep the existing seven-byte DS4Windows feedback protocol and accepted-command/reconnect ownership guarantees.
- Align virtual DS4 acceleration defaults and conversion helpers with the existing calibration. Raw physical-controller motion forwarding remains unchanged.
- Retain virtual keyboard keys and mouse buttons during idle polls without repeating relative mouse movement or scrolling.
- Update VIIPER's existing dependencies and build toolchain while retaining DS4Windows-managed installation, updates and USB/IP scheduling.

#### Updating

- The complete installer and portable ZIP include the matching VIIPER executable, source, notices and verification records. Close games, DS4Windows and VIIPER before updating. USB/IP remains at 0.9.8.1; no driver version change is required.
- The maintainer successfully tested the paired changes before release. This release remains unsigned; no Authenticode signature is claimed.

### [5.0.24](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.24) - 2026-10-14

#### Fixed

- Respect the profile's Normal lightbar mode when forwarding native DualSense/DualSense Edge feedback. Applications can no longer replace the profile colour through native USB or Bluetooth output, including cached Bluetooth audio state. Switching back from Pass-through restores the profile colour without requiring another application packet. Preserve native haptics, adaptive triggers, player LED control and explicit DSX overrides ([#28](https://github.com/Pav-Osmolski/DS4Windows/issues/28)).

- Prevent a rare DSX UDP listener crash when socket reception races rapid stop/restart or disposal. Retain the exact session socket through its receive loop.

#### Documentation

- Add optional Ko-fi support links for ongoing maintenance of this independent fork.

### [5.0.23](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.23) - 2026-10-14

#### Fixed

- Allow a disconnected DualShock 3 connection to finish cleanup once its input worker has stopped, without requiring a final physical output report over the lost transport. This addresses a remaining reconnect failure from [#25](https://github.com/Pav-Osmolski/DS4Windows/issues/25). Keep worker-stop checks and final physical neutral output for controllers that are still connected. Hardware confirmation remains pending.

### [5.0.22](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.22) - 2026-10-14

#### Fixed

- Keep DS3/DS4 cleanup with its legacy controller owner when stopping the shared input service. Switch 2 cleanup no longer incorrectly quarantines those connections, blocking Stop/Start ([#25](https://github.com/Pav-Osmolski/DS4Windows/issues/25)). Report incomplete disconnect cleanup in the normal log to help diagnose remaining reconnect failures.
- Prevent Output Slots' horizontal scrollbar from flashing during auto-fit window resizing. Retain horizontal scrolling for narrow windows and saved manual column widths.
- Center the Profile Editor's launch-program controls, add consistent spacing, and show the program icon without an empty button border.
- Give the controller readings dock beside Axis Config enough width for its cards, margins and scrollbar, preventing clipped right edges when Show controller readings is enabled ([#19](https://github.com/Pav-Osmolski/DS4Windows/issues/19)).

#### Diagnostics

- Preserve a unique USB/IP uninstall log across reboot/repair attempts and include hardware IDs in post-reboot device diagnostics ([#20](https://github.com/Pav-Osmolski/DS4Windows/issues/20)). This improves investigation of incomplete removal; it does not bypass checks for live drivers or claim to resolve the reported installation failure.

### [5.0.21](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.21) - 2026-10-13

#### Fixed

- Restore Axis Config scrolling by bounding its viewport to the available editor height ([#19](https://github.com/Pav-Osmolski/DS4Windows/issues/19)). Add optional live Controller Readings beside the axis controls, stacking below them in narrow windows without creating another polling owner.
- Distinguish confirmed non-present USB/IP device records from live or unclassified root devices during post-reboot verification ([#20](https://github.com/Pav-Osmolski/DS4Windows/issues/20)). Log device presence/problem codes and driver states; preserve the reboot boundary and rejection of running drivers or retained driver packages. The reported machine still needs validation to establish whether its remaining root record is non-present.

#### Documentation and maintenance

- Expand the documentation index and practical guides for controller/output compatibility, Profile Editor, audio/feedback, Nintendo options and integrations. Document Audio Haptics, Trigger Lab, Auto Profiles and diagnostic controls in more detail, use all available documentation images, and correct setup/update/build instructions.
- Refresh app screenshots to show the current interface, including Output Slots and Log.
- Upgrade GitHub Actions Python setup from v5 to v7 and caching from v4 to v6 through reviewed Dependabot PRs [#21](https://github.com/Pav-Osmolski/DS4Windows/pull/21) and [#22](https://github.com/Pav-Osmolski/DS4Windows/pull/22). Retain the pinned .NET SDK, Python version, dependency locks and complete validation.
- Release 5.0.21 with matching app and installer version 5.0.21.0. The maintainer tested the Axis Config/readings changes successfully before publication.

### [5.0.20](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.20) - 2026-10-13

#### Changed

- Make the Bluetooth impulse-release test robust to delayed hosted callbacks, with controlled-time fade and late-callback checks plus a real-timer neutral-output check. Preserve runtime timing and output ownership.
- Build one downloadable package from the proposed merge when a branch has an open PR, while retaining separate branch and merge tests and downloads for branches without PRs.
- Align local and hosted SDK selection, add reviewed NuGet lock files across all projects and monitor .NET SDK servicing updates with Dependabot. Extract the shared UI test dispatcher and add installer-stage timing diagnostics without skipping validation.
- Refresh Special Actions with a wrapping toolbar, a separate Enabled column and readable, wrapping action names and controller shortcuts. Preserve action editing, export, activation and Trigger Lab protection.
- Cache NuGet dependencies, remove redundant restores, cancel superseded CI revisions and retain test reports and hang diagnostics. Run both .NET test suites against the exact release source before packaging; retain installer integrity and upgrade checks.

- Improve Output Slots with explicit slot numbers, clearer column headings, wrapping controller names, fit-to-window controls and remembered manual column widths. Replace the unused XInput column and label input binding accurately.
- Center the Lightbar RGB slider rail and thumb using a dedicated template, preserving mouse dragging, keyboard adjustment and color values.
- Redesign Controller Readings into wrapping status, stick, trigger, motion and touchpad cards with clear Input/Output values, plot legends, read-only sensor bars and a fully visible report interval. Preserve live snapshot capture and plot coordinates.
- Center RGB sliders within their color surfaces, replace the always-rainbow button artwork with a rainbow icon, and add breathing room above Touchpad rotation.
- Organize Profile Editor Advanced options into Output, Feedback, Audio, Input and Connection categories with full-width cards, consistent controls and preserved controller-specific settings.
- Align footer messages vertically, standardize profile toolbar button padding, and align the Dual Joy-Con aiming heading with the Nintendo feature cards.
- Make profile-editor timing diagnostics opt-in through Settings > Updates & tools. Persist the separate setting, which defaults to off.
- Redesign Axis Config, Lightbar, Touchpad and Gyro with focused cards, compact fields and consistent spacing. Preserve all output modes, controller settings and calibration actions; remove decorative lines around mode pages.
- Use a scalable, theme-aware Keep size icon and confirmation checkmark so both remain visible in dark and light themes.

### [5.0.19](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.19) - 2026-10-12

#### Fixed

- Incorporate [upstream PR #138](https://github.com/hbashton/DS4Windows/pull/138) by meiameiameia: report profile save failures, keep the editor open, and preserve the original file during a failed rename.
- Keep user-entered XML suffixes, stored filenames, profile selections and creation events consistent, including profiles whose existing names end in `.xml`.
- Retain failed Overview changes as snapshots tied to their original profiles. Offer explicit retry or discard instead of announcing success or automatically retrying a locked file. Refuse stale retries after a profile has been edited or deleted.
- Report Rename collisions and file errors without closing the rename dialog or changing the selected name. Preserve file contents and update active controller selections only after a successful rename.

#### Maintenance

- Align Windows management dependencies in the application and installer helpers to System.Management 10.0.12. Keep the existing .NET 8 target and driver-detection behaviour.
- Remove the application's redundant direct System.Memory package reference; use the implementation provided by the .NET runtime.
- Update MSTest framework/adapter to 4.5.1 and Microsoft.NET.Test.Sdk to 18.10.1. Preserve exact exception assertions, test case coverage and existing hardware opt-in skips.
- Repair standalone USB hardware-verifier build dependencies and run its 75 existing unit tests in CI.
- Configure weekly Dependabot proposals for the application's C# projects, installer helpers, tests, tools and GitHub Actions. Add a maintenance guide covering package review priorities and manual coordination of WiX tooling and bundled drivers. Updates remain subject to review and CI; automatic merging is not enabled.

### [5.0.18](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.18) - 2026-10-12

#### Added

- Search all Settings sections, remember the last section and show language/executable restart reminders.
- Report installed/portable deployment mode in the startup log.
- Illustrate the user guide with app screenshots and refresh the Settings image.

#### Changed

- Reorganize Settings into General, Appearance, Controllers, Updates & tools, Integrations, and Advanced sections. Group related options into focused cards with consistent inputs and action buttons; keep Nintendo setup collapsed until needed.
- Bring language settings, update checks, controller setup, useful tools and logging shortcuts within easier reach while preserving existing settings and behavior.
- Let Settings use the full available window width, matching the other app pages.

### [5.0.17](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.17) - 2026-10-11

#### Fixed

- Align the Swipe Touchpad To Switch Profiles checkbox and label with the surrounding Settings options.
- Remove inner dropdown item borders in both themes and let menu rows fill the available width. Keep selection and keyboard highlights, disabled states, and scrolling for longer lists.
- Let the Log footer size to its buttons so they remain fully visible, and center the entry count beside them.
- Read USB/IP driver-package versions from the installed INF files when the SYS files have no version resource.
- Log USB/IP installed/required versions, driver running states, runtime readiness and package verification. Include the HidHide driver version when detected and report VIIPER readiness from the actual prerequisite check.
- Give the Log tab's Export and Clear buttons the same padding and minimum height as other secondary buttons.

#### Added

- Copy Text in the Log tab copies the latest entries with timestamps as plain text, using the same format as Export.

#### Changed

- Release **5.0.17**, with app and installer version **5.0.17.0**. The maintainer tested the combined UI and logging changes successfully before release.

### [5.0.16](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.16) - 2026-10-11

#### Fixed

- Align the controller card's lightbar colour control with its Edit button using a shared layout row.
- Give Profiles and Auto Profiles search fields consistent height and padding; make typing cursors readable in both themes, including other text inputs and dark-theme numeric fields.
- Keep compact dropdowns at a consistent minimum height, including an empty Controller 1 selection in Auto Profiles.
- Match the profile-switch notification checkbox alignment, text gap and bottom spacing to the other Settings checkboxes.
- Apply the same padded button style to Device Options and Config Swipe Profiles, allowing translated Device Options labels to fit.
- Separate Run As options from the group heading and vertically center the Auto Profiles window-title placeholder.

#### Changed

- Replace the sidebar letter badge with the maintainer-provided DS4Windows PNG icon.
- Release **5.0.16**, with app and installer version **5.0.16.0**. The maintainer tested the UI changes successfully before publication.

### [5.0.15](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.15) - 2026-10-11

#### Fixed

- Support usbip-win2 0.9.8.1 with the matching VIIPER backend, resolving [#4](https://github.com/Pav-Osmolski/DS4Windows/issues/4). The maintainer tested the combined release successfully before publication.
- Issue automation ignores unrelated labels before starting a job, and uses a current action compatible with longer GitHub tokens. Shipped fixes close as completed; duplicate/out-of-scope reports close as not planned.

#### Changed

- Upgrade the bundled USB/IP stack to usbip-win2 **0.9.8.1** and VIIPER **0.1.10**, with the new native request layouts and low-latency controller imports.
- Verify the matching USB/IP library as well as its executable and drivers; accept the new port-list format without weakening import ownership checks.
- Release **5.0.15**, with app and installer version **5.0.15.0**. See the [USB/IP upgrade guide](docs/usbip-upgrade.md) for required restarts and the upstream serial-override limitation.
- Consolidated the remaining `doc` pages under `docs`, refreshed Moonlight / Sunshine instructions, and moved the DSX diagnostic script into `utils`.
- Removed the obsolete upstream batch installer and unused root screenshots.
- Updated About and Moonlight help links for this fork, while retaining upstream credits.
- Issue labels still close duplicate, out-of-scope and shipped-fix reports, but no longer lock conversations or automatically close reopened reports.

### [5.0.14](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/5.0.14) - 2026-10-10

#### Fixed

- Preserve the previous profile when a save is interrupted ([upstream #129](https://github.com/hbashton/DS4Windows/pull/129), meiameiameia).
- Keep virtual output slots registered when disconnect fails, allowing a retry without reusing the occupied slot ([upstream #130](https://github.com/hbashton/DS4Windows/pull/130), meiameiameia).
- Respect touchpad-click remapping and passthrough settings in emulated DS4, DualSense and DualSense Edge reports ([upstream #131](https://github.com/hbashton/DS4Windows/pull/131), meiameiameia).
- Remove the controller properly after a Bluetooth disconnect special action, allowing reconnection without Stop/Start ([upstream #132](https://github.com/hbashton/DS4Windows/pull/132), meiameiameia).
- Reuse the profile XML serializer to avoid repeatedly generating assemblies and reduce profile-switch overhead ([upstream #134](https://github.com/hbashton/DS4Windows/pull/134), petemess95).

#### Changed

- Retired RC release names. New releases use numeric versions and are published as **Latest**, with the prerelease flag cleared.
- App and installer versions advance to **5.0.14.0**; release and package version **5.0.14**. Installer: `DS4Windows_5.0.14.0_Setup_x64.exe`; portable package: `DS4Windows_5.0.14_x64.zip`.
- Continue unsigned releases with exact-source build records and checksum verification. See the [release process](docs/release-process.md).

#### Upgrade notes

DS4Updater 2.0.9 already supports numeric releases and the schema 1 build
receipt. Existing fork builds with fork update support can update to 5.0.14.
RC4.6.7 users still need one manual upgrade because that build checks upstream.
The pinned VIIPER and USB/IP packages are unchanged. Every new release must
increment the numeric app and installer version.

### [VIIPERRC4.6.8](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/VIIPERRC4.6.8) - 2026-10-10

#### Changed

- App update checks and changelogs use `Pav-Osmolski/DS4Windows`.
- Updater discovery and downloads use `Pav-Osmolski/DS4Updater` and require DS4Updater 2.0.9 or newer for both portable and installed updates. Existing hash, size, release identity, and ownership checks are retained.
- App assembly/file and installer product/bundle versions advance to **5.0.13.0**. The setup filename is `DS4Windows_5.0.13.0_Setup_x64.exe`.
- Removed the Settings donation panel and its link to the upstream developer's PayPal account. Contributor credits remain.
- Shortened the README and moved release history into this changelog.

#### Upgrade notes

RC4.6.7 users must install the first app build with fork update support
manually once. Replacing DS4Updater alone does not change RC4.6.7's upstream
update checks. Later updates from the new app use this fork.

DS4Updater [2.0.9](https://github.com/Pav-Osmolski/DS4Updater/releases/tag/v2.0.9)
is published. RC4.6.8 was tested and published successfully. Its changes are included in
5.0.14. The pinned VIIPER and USB/IP packages are unchanged from RC4.6.7.

### [VIIPERRC4.6.7](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/VIIPERRC4.6.7) - 2026-10-09

#### Fixed

- USB/IP driver verification timeouts at startup, reported in upstream issues [#118](https://github.com/hbashton/DS4Windows/issues/118) and [#126](https://github.com/hbashton/DS4Windows/issues/126).
- Windows version logging, including Windows 11 display versions and full build revisions.
- Missing profile editor navigation icons.
- Blurry system tray icons at small sizes and different display scaling levels.

#### Changed

- Grouped translation folders under `Lang`.
- Updated fork download, setup, and project links and retained upstream contributor credits.

#### Release notes

Unsigned Windows x64 release candidate with binary/installer version
**5.0.12.0**. Passed 7,155 automated tests (12 skipped, zero failures), plus
translation and installer lifecycle checks. The fixes were tested successfully
on the affected hardware.

### Upstream release history

These releases were published by [hbashton/DS4Windows](https://github.com/hbashton/DS4Windows/releases)
before this fork's first release, **VIIPERRC4.6.7 on 9 October 2026**. Each entry
links to its original release notes; dates are GitHub publication dates in UTC.
Summaries retain the scope and limitations reported at the time, rather than
presenting historical installation instructions as current advice.

Upstream published its own **VIIPERRC4.6.7** later on the same day, after this
fork's first release. It is therefore outside this inherited history; the
identical tag names belong to different repositories. RC4.4 development is
covered under RC4.5, which included that work without a separate published
RC4.4 release.

<a id="inherited-release-notes-rc466"></a>

### [VIIPERRC4.6.6](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6.6) - 2026-09-23

#### Improved

- Improve VIIPER startup and repair, allow slow prerequisite checks to finish, and identify the failed startup stage in error messages.
- Prevent competing installed instances and keep DS4Windows open through recovery attempts.
- Correct the `-autolaunch` update path for installed and portable copies while retaining profiles and settings.
- Improve DualSense Edge identification, calibration checks and virtual reports; share native haptics and adaptive-trigger feedback across standard and Edge emulation.
- Reject unsupported Edge onboard-profile commands. Virtual onboard-profile editing and complete USB/Bluetooth hardware acceptance were not included.

<a id="inherited-release-notes-rc465"></a>

### [VIIPERRC4.6.5](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6.5) - 2026-09-23

#### Fixed

- Prevent Log-page freezes after long sessions and improve scrolling, clearing and export.
- Restore the profile lightbar colour after native DualSense game feedback ends.
- Resolve update waits caused by competing installations and repair missing or mismatched VIIPER without closing DS4Windows.
- Keep portable backend repair beside the portable application and remove VIIPER's separate update prompts.

#### Documentation

- Refresh the getting-started guide.

<a id="inherited-release-notes-rc464"></a>

### [VIIPERRC4.6.4](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6.4) - 2026-09-22

#### Improved

- Refine Bluetooth advanced-haptics conversion for DualSense and DualSense Edge when emulating DualSense.
- Reduce gaps between adjacent effects and prevent unrelated settings updates from cutting rumble short.
- Preserve explicit stop commands and allow trigger and lighting updates alongside active rumble.
- Bundle the matching VIIPER update. Advanced effects still depend on game support.

<a id="inherited-release-notes-rc463"></a>

### [VIIPERRC4.6.3](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6.3) - 2026-09-21

#### Fixed

- Stop repeated temporary-profile activation while a switch is held, and cancel pending activation when the button is released early.
- Keep the current mapping active during preparation, preserve held keys, and reuse the virtual controller for same-output profile changes.
- Reject delayed work from old connections or superseded profile selections.
- Track mouse toggles and held buttons by their binding or macro owner, so one action cannot release another's click.
- Correct FakerInput X1/X2 translation and retire macro input safely during disconnect and Stop.

#### Release notes

- Automated mapper and lifecycle checks did not establish acceptance on the reporter's hardware or guarantee mixed controller/mouse input in every game.

<a id="inherited-release-notes-rc462"></a>

### [VIIPERRC4.6.2](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6.2) - 2026-09-14

#### Added

- Add optional local DSX game-mod support for physical DualSense/Edge triggers and lighting, disabled by default. Preserve per-trigger Trigger Lab priority and restore underlying state when overrides end. This was not full DSX or PCM-audio compatibility.

#### Fixed

- Preserve custom portable EXE names with DS4Updater 2.0.7 while retaining package checks, rollback and user files.
- Separate requested startup preferences from tasks temporarily disabled for repair or a restart; add a verified continuation after reboot.
- Preserve extras-only shifted bindings through save/load and release their effects correctly.

#### Maintenance

- Expand settings, macro and profile-migration coverage. Credit @Gabarsolon and @anagnorisis2peripeteia (Cameron Beeley) for the incorporated contributions.
- Physical reboot/logon recovery and game-mod acceptance remained outstanding in the release notes.

<a id="inherited-release-notes-rc461"></a>

### [VIIPERRC4.6.1](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6.1) - 2026-09-14

#### Added

- Add a configurable 360-degree flick-stick calibration test in Axis Config. Reserve the selected button while configured and cancel unfinished turns when the profile or connection changes.

#### Fixed

- Guard HidHide against Windows app-execution aliases that can crash its configuration client, with a confirmed, backed-up repair for affected entries.
- Back up and recover the reserved startup tasks; report registration failures without aborting installation or bypassing backend checks.

#### Changed

- Remove legacy trigger-effect controls from Axis Config while retaining saved effects and Trigger Lab.
- Keep calibration dependent on in-game mouse sensitivity; the HidHide fix addressed the confirmed alias failure, not every configuration-client crash.

<a id="inherited-release-notes-rc46"></a>

### [VIIPERRC4.6](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.6) - 2026-09-14

#### Fixed

- Recover DualSense speaker startup after source changes, preserve Sonar-compatible capture, and convert USB audio to the physical output rate correctly.
- Prevent samples and callbacks from retired audio sources reaching replacement sessions.
- Improve DS4 Bluetooth motor-change delivery, retain failed DS3 output for retry, and preserve ordered feedback and stop commands through VIIPER.
- Coalesce only eligible pending Nintendo feedback duplicates without retuning rumble gain or cadence.
- Complete manual Bluetooth removal and reject stale Game Bar activation after profile policy changes.

#### Release notes

- Bundle VIIPER 0.1.5-rc4.6. The rare movement-interruption report remained unattributed; automated checks did not establish its cause.

<a id="inherited-release-notes-rc459"></a>

### [VIIPERRC4.5.9](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.9) - 2026-09-12

#### Improved

- Give native DualSense Bluetooth game feedback a separate 2 ms software pacing ceiling, retaining command order and distinct stop/effect transitions.
- Merge only eligible identical pending state commands to reduce backlog; preserve adaptive-trigger payloads, unknown commands and ownership changes.

#### Release notes

- Controlled body-rumble bursts preserved all 251 changes and reduced final-stop submission delay from 252 ms to approximately 0.15-0.18 ms. These were host-side measurements, not radio or actuator latency, a hardware-rate guarantee, or adaptive-trigger gameplay acceptance.

<a id="inherited-release-notes-rc458"></a>

### [VIIPERRC4.5.8](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.8) - 2026-09-11

#### Fixed

- Prevent Bluetooth audio packets from clearing accepted native DualSense rumble mode and motor state.
- Stop local lighting/trigger updates and stale media templates from overwriting newer game feedback.
- Remove a circular wait when disconnect is queued from the controller worker, while preserving completion and final-output cleanup.

#### Release notes

- Retain the existing PCM encoding, audio cadence and Nintendo tuning. Physical connection and streaming observations were not complete in-game haptics or trigger acceptance.

<a id="inherited-release-notes-rc457"></a>

### [VIIPERRC4.5.7](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.7) - 2026-09-11

#### Fixed

- Correct the Bluetooth DualSense feedback regression introduced in RC4.5.6: advance ordered game commands between audio packets rather than letting them accumulate behind the audio clock.
- Allow startup to progress with partially filled audio buffers.
- Deliver live Trigger Lab changes during audio streaming without unrelated profile edits clearing native effects; retain independent ownership of each trigger.

#### Release notes

- Physical Bluetooth haptics were checked; adaptive-trigger gameplay confirmation remained outstanding.

<a id="inherited-release-notes-rc456"></a>

### [VIIPERRC4.5.6](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.6) - 2026-09-11

#### Added

- Add Audio Haptics for Switch 2 Pro and Joy-Con 2, including Mix/Replace and application/system sources across active audio outputs.

#### Improved

- Support encrypted VIIPER connections on Windows 10 without relying on Windows 11's native encryption provider.
- Share setup ownership between cleanup and repair, release listeners after failed initialization, and improve portable startup diagnostics.
- Preserve native DualSense command order through busy writers with bounded retries, and refine sustained Switch 2 rumble framing.
- Roll back failed capture, discard old-source audio and retire failed audio runtimes without clearing unrelated native feedback.
- Bundle VIIPER 0.1.4-rc4.5.6 and its matching dependencies.

<a id="inherited-2026-09-09-rc455-release-notes"></a>

### [VIIPERRC4.5.5](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.5) - 2026-09-10

#### Fixed

- Recover verified legacy VIIPER startup registrations that could cause setup error `0x80070001`, saving the original task first.
- Prevent portable preferences from redirecting installed startup tasks or removing their ownership markers.
- Align application and installer startup priority, update owned tasks in place, and identify unsafe task conflicts clearly.
- Retain the earlier Stop and read-failure recovery fixes without changing controller transport or rumble.

<a id="inherited-2026-09-09-rc454-release-notes"></a>

### [VIIPERRC4.5.4](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.4) - 2026-09-09

#### Fixed

- Recover from terminal DS4 Bluetooth input failures without requiring another potentially stalled physical output write.
- Avoid submitting a new Bluetooth audio effect during failed-read cleanup.
- Cover read failure occurring before Stop, alongside the existing shutdown and reconnect protections.

#### Release notes

- Improve recovery from the failure; the initiating cause of the reporter's unexpected error 995 remained unconfirmed.

<a id="inherited-2026-09-09-rc453-release-notes"></a>

### [VIIPERRC4.5.3](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.3) - 2026-09-09

#### Fixed

- Remove the shutdown/removal deadlock that could leave Stop disabled, and prevent delayed cleanup from removing a replacement connection.
- Distinguish expected read cancellation during Stop from unexpected errors, and prevent workers from joining or interrupting themselves.
- Reject hotplug admission while the same controller is being removed.

#### Improved

- Use DS4Updater 2.0.6's verified release metadata instead of a per-release candidate allowlist.
- Retain diagnostics for unexpected read failures; this release did not identify their initiating cause.

<a id="inherited-2026-09-09-rc452-release-notes"></a>

### [VIIPERRC4.5.2](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.2) - 2026-09-09

#### Added

- Bring original Joy-Cons into the shared Link/Unlink workflow, with remembered same-generation pairs, manual orientation and applicable Nintendo aiming options.
- Rename Switch 2 Controls to Nintendo Options, add optional live remapping readings, and expose missing controller buttons as Special Action triggers.
- Add a separate Xbox impulse-trigger translation setting and safe portable updates through DS4Updater 2.0.5.

#### Fixed

- Prevent stale DualSense compatibility rumble from restarting on Nintendo controllers after a stop or non-rumble update.
- Preserve unknown saved Special Action trigger names, and improve paired disconnect, unlink and right-Joy-Con stick-assist handling.

#### Release notes

- Original Joy-Cons did not gain Joy-Con 2 optical hardware or the raw-stick calibration wizard. Physical rumble acceptance remained incomplete.

<a id="inherited-release-candidate-4-5-1"></a>

### [VIIPERRC4.5.1](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5.1) - 2026-09-09

#### Fixed

- Stop finite Switch 2 connection/identification effects from repeating indefinitely.
- Improve sustained rumble recovery after output contention without duplicate one-shots or stale backlog.
- Keep held-output maintenance alive after temporary worker failures.
- Include the Xbox One/Series output identity configuration in both packages, correcting activation failures for profiles selecting Xbox output.

#### Release notes

- Keep the existing rumble strength, codec and translation paths; this was a timing and effect-lifetime correction.

<a id="inherited-release-candidate-4-4"></a>

<a id="inherited-release-candidate-4-5"></a>

### [VIIPERRC4.5](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.5) - 2026-09-09

#### Added

- Add Switch 2 Pro USB/Bluetooth input, standalone or linked wireless Joy-Con 2, and Xbox One/Series virtual output through the shared mapper.
- Add remembered Joy-Con activation, Link/Unlink controls, orientation, optical mouse and per-hand/dual-Joy-Con aiming.
- Expand Nintendo remapping artwork, live readings, C-button support, gyro handling and persistent controller identities.
- Bundle a matching portable VIIPER, reusing only verified compatible instances and preserving sessions borrowed from another owner.

#### Improved

- Separate Xbox body and impulse feedback, refine DualSense-to-HD-rumble translation, and improve sustained rumble, output-slot cleanup and profile-effect restoration.

#### Release notes

- Include development work described as RC4.4; no separate RC4.4 release appears in upstream's published release list.
- Switch 2 USB headset output had physical evidence; Bluetooth headset audio was unsupported and headset microphone capture unverified. HD-rumble translation was not lossless DualSense waveform reproduction.
- The backend remained VIIPER plus USB/IP. See the [detailed historical assessment](https://github.com/hbashton/DS4Windows/blob/VIIPERRC4.5/docs/RELEASE_CANDIDATE_4_5.md) for validation boundaries.

<a id="inherited-release-candidate-4-3"></a>

### [VIIPERRC4.3](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.3) - 2026-08-09

#### Improved

- Align VIIPER 0.1.0 identity across its metadata, API, packages and compatibility checks.
- Accept portable backends by their verified binary identity rather than requiring Program Files.
- Retire stale installer registrations and require repair for missing, outdated or modified backend files.
- Preserve healthy shared infrastructure and restrict uninstall cleanup to installation-owned files and tasks.
- Expand clean-install, update, repair, uninstall, recovery and payload verification.

<a id="inherited-release-candidate-4-2"></a>

### [VIIPERRC4.2](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.2) - 2026-08-07

#### Improved

- Reduce VIIPER CPU usage through adaptive latest-state input delivery instead of redundant busy loops, retaining a 1,000 Hz software ceiling for fresh input.
- Retire temporary Game Bar companions before exposing native Xbox output and preserve DS4 Bluetooth ownership through temporary input droughts.
- Correct per-application DS4 audio consumption and preserve selected speaker sources when native games take feedback ownership.
- Recover audio capture after application, source and output-device changes; follow the installed stable/prerelease update channel.
- Include all localized assemblies, improve in-app VIIPER repair, bound installer busy retries and correct startup-task principals.
- Keep scrollbars clear of controls on Audio Haptics, Trigger Lab, Auto Profiles and Log.

<a id="inherited-release-candidate-4-1"></a>

### [VIIPERRC4.1](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4.1) - 2026-08-06

#### Improved

- Share preallocated input, audio and haptics paths with fewer handoffs; submit fresh virtual DualSense input up to a 1,000 Hz software ceiling.
- Combine game feedback and local overrides without replaying old state, restore profile feedback when the owning game exits, and synchronize rumble delivery.
- Recover application audio capture after source/device changes and make rapid profile changes prefer the newest selection.

#### Fixed

- Correct two-finger swipe switching, transient Auto Profile failures, first-run Light theme resources and microphone timeout ownership.
- Prevent closed dropdowns from changing settings while scrolling the page.

#### Installation

- Bundle VIIPER 0.0.8 with offline setup, serialize installation and repair, enforce incompatible-driver reboot boundaries, and preserve files outside package ownership.

### [VIIPERRC4](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC4) - 2026-08-05

#### Added

- Introduce DS4Windows 5.0.0.0 with an offline x64 installer for the application, VIIPER 0.0.7 and USB/IP, plus optional HidHide/FakerInput.
- Add a live Overview workspace, searchable profile lists, a dedicated editor and controller-aware remapping artwork.
- Expand Trigger Lab with linked/split effects, independent trigger control, saved presets, previews and full-pull actions.
- Add profile-based Audio Haptics with application/system/game sources, Mix/Replace and controller/AUX playback options.

#### Improved

- Rework DualSense audio and native feedback scheduling, speaker/headset routing, microphone recovery and game/profile ownership transitions.
- Preserve PlayStation audio features across Xbox/Switch virtual outputs and improve DS4 Bluetooth audio recovery.
- Harden profile switching, Game Bar compatibility, backend verification and installer-owned update/repair/uninstall behaviour.

### [VIIPERRC3](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC3) - 2026-07-25

#### Fixed

- Harden VIIPER component installation, including backend source selection and service/startup-task registration.

### [VIIPERRC2](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC2) - 2026-07-25

#### Fixed

- Correct DS4 Bluetooth transport cadence and queue saturation using the `0x12` report path.
- Fix DualSense/DS4 AUX routing, distorted application playback, and Audio Haptics startup, persistence and Xbox-emulation behaviour.
- Prevent stuck Trigger Lab vibration and preserve lightbar state.
- Improve restart/reconnect handling, UI startup ordering and VIIPER installation.

### [VIIPERRC1](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERRC1) - 2026-07-24

#### Improved

- Consolidate the VIIPER beta work into a release candidate with stabilized DS4 Bluetooth audio timing and queue handling.
- Improve DualSense/DS4 speaker, microphone and haptics routing across physical and virtual output combinations.
- Expand profile-persisted Audio Haptics and Trigger Lab controls.
- Modernize Overview, profile selection, search, editing and controller-aware remapping.
- Harden Game Bar companion behaviour, backend lifecycle, HidHide sessions and installer/update paths.

#### Release notes

- The upstream notes were a cumulative summary since 4.0.2.3, including beta work already released, rather than a list of features introduced only in RC1.

### [VIIPERBeta9](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta9) - 2026-07-24

#### Added

- Add automatic game detection for Audio Haptics.

#### Improved

- Retain PlayStation audio/features across Xbox and Switch virtual outputs, update real-time DS4 transport and support prerelease-channel updates.

### [VIIPERBeta8](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta8) - 2026-07-24

#### Release notes

- Publish the next VIIPER beta. Upstream supplied a [comparison with Beta 7](https://github.com/hbashton/DS4Windows/compare/VIIPERBeta7...VIIPERBeta8) without a separate feature summary.

### [VIIPERBeta7](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta7) - 2026-07-23

#### Improved

- Make update checks follow the installed prerelease channel, switching to stable when a newer stable release becomes available.

### [VIIPERBeta6](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta6) - 2026-07-23

#### Changed

- Publish the redesigned interface and expanded VIIPER feature work for beta testing.
- Identify DS4 audio and Special Action profile hot-swapping as remaining areas of work in the release notes.

### [VIIPERBeta5](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta5) - 2026-07-17

#### Added

- Add DualSense microphone support over Bluetooth, exposed as Headset Microphone (Wireless Controller). Upstream reported testing it in Discord.

### [VIIPERBeta4](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta4) - 2026-07-16

#### Fixed

- Restore speaker output and vibration for physical DualSense controllers emulating a DualSense.

### [VIIPERBeta3](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta3) - 2026-07-14

#### Improved

- Reduce DualSense controller-speaker latency, with upstream reporting Bluetooth gameplay testing in Assassin's Creed Black Flag Resynced.

### [VIIPERBeta2](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta2) - 2026-06-24

#### Added

- Add DualSense Bluetooth advanced haptics, controller-speaker audio and headset output, including game and selected audio sources.
- Allow the mute button to cycle configured profiles.

### [v4.0.2.3](https://github.com/hbashton/DS4Windows/releases/tag/v4.0.2.3) - 2026-06-14

#### Fixed

- Re-enable persistent HidHide configuration writes and correct Moonlight virtual DS4 v2 recognition.

### [VIIPERBeta](https://github.com/hbashton/DS4Windows/releases/tag/VIIPERBeta) - 2026-06-14

#### Added

- Introduce the Alia5/VIIPER backend for virtual DualSense and Switch 2 Pro controllers.
- Forward native adaptive-trigger feedback wirelessly and add mute-light/button profile controls.

#### Fixed

- Correct DualSense gyro handling.

#### Release notes

- Controller-speaker audio and advanced haptics were not yet included in this first VIIPER beta.

### [v4.0.2.2](https://github.com/hbashton/DS4Windows/releases/tag/v4.0.2.2) - 2026-06-14

#### Fixed

- Move Game Bar detection to its own probe and handle systems without `ms-gamebar`.
- Harden application startup.
- Show full program filenames in Special Actions and include PowerShell scripts in the program picker.

### [v4.0.2.1](https://github.com/hbashton/DS4Windows/releases/tag/v4.0.2.1) - 2026-06-11

#### Added

- Add OpenRGB integration and session-scoped HidHide blacklisting, credited upstream to @anagnorisis2peripeteia.

#### Fixed

- Initialize mapped mouse output when a profile starts. Upstream associated this fix with the reported profile-switch crash, without establishing a separate confirmed cause.

### [v4.0.2](https://github.com/hbashton/DS4Windows/releases/tag/v4.0.2) - 2026-06-07

#### Fixed

- Prevent crashes with duplicate profiles using specific device identifiers.
- Reduce lag when releasing a trigger used to switch profiles.

### [v4.0.1](https://github.com/hbashton/DS4Windows/releases/tag/v4.0.1) - 2026-06-06

#### Fixed

- Incorporate a contributor fix for third-party controller lightbars.

#### Changed

- Simplify the system-tray title.

### [v4.0.0](https://github.com/hbashton/DS4Windows/releases/tag/v4.0.0) - 2026-06-06

#### Added

- Add window-title-only and duplicated Auto Profile rules with per-device matching.
- Give Game Bar profiles priority while Game Bar is visible.
- Include per-profile DualSense adaptive-trigger settings.

#### Fixed

- Correct full-pull trigger actions and improve layout for long executable paths.

#### Changed

- Publish hbashton's first stable revived-fork release and direct application/updater checks to its repositories.

### [3.9.9Beta3Hotfix](https://github.com/hbashton/DS4Windows/releases/tag/3.9.9Beta3Hotfix) - 2026-06-06

#### Fixed

- Give Game Bar profile rules priority over ordinary Auto Profiles.

### [3.9.9Beta3](https://github.com/hbashton/DS4Windows/releases/tag/3.9.9Beta3) - 2026-06-05

#### Added

- Add per-profile Game Bar switching to an XInput-friendly profile when opening the overlay.

#### Improved

- Refine dual-stage trigger threshold detection.

### [3.9.9Beta2](https://github.com/hbashton/DS4Windows/releases/tag/3.9.9Beta2) - 2026-06-02

#### Added

- Add per-profile DualSense adaptive-trigger settings and separate soft/full-pull actions.
- Add profile audio-passthrough modes, with connection-dependent audio-device availability.

### [3.9.9Beta](https://github.com/hbashton/DS4Windows/releases/tag/3.9.9Beta) - 2026-05-31

#### Added

- Configure which profiles participate in two-finger swipe switching independently of Auto Profiles.

#### Fixed

- Track created virtual-controller identities to prevent duplicate detection through Moonlight Virtual Controller Support.

#### Release notes

- Per-device Auto Profile selection was described as planned work, not a completed feature in this release.
