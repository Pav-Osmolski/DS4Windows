# DS4Windows documentation

Guides for the current Windows x64 app. Start with setup, then choose a feature
or app page below. Screenshots illustrate the interface; their selected options
are examples, not recommended settings for every controller.

## Start here

| Task | Guide |
| --- | --- |
| Install, connect or update | [Getting started](getting-started.md), including portable setup and profile backups |
| Choose a physical controller and virtual output | [Compatibility and feature limits](controller-compatibility.md) |
| Fix detection, double input, missing feedback or updates | [Troubleshooting](troubleshooting.md) |
| Collect information for an issue | [Log and diagnostics](user-guide.md#log-and-diagnostics), including the [XInput controller test](user-guide.md#xinput-controller-test) |
| Upgrade the bundled backend | [USB/IP upgrade](usbip-upgrade.md): matching usbip-win2 0.9.8.1 / VIIPER 0.1.12 and restart requirements |

## Explore the app

See the [fresh screenshot gallery](screenshots.md) for Profile Editor, Audio
Haptics, Trigger Lab, Appearance and the Log page.

![DS4Windows overview](images/tour/overview.png)

The [illustrated user guide](user-guide.md) covers the app's pages:
[Overview](user-guide.md#overview), [Controllers](user-guide.md#controllers),
[Profiles](user-guide.md#profiles), [Audio Haptics](user-guide.md#audio-haptics),
[Trigger Lab](user-guide.md#trigger-lab), [Auto Profiles](user-guide.md#auto-profiles),
[Output Slots](user-guide.md#output-slots), [Settings](user-guide.md#settings)
and [Log](user-guide.md#log-and-diagnostics).

## Configure a feature

| Guide | What it covers |
| --- | --- |
| [Profile Editor](profile-editor.md) | Saving, button mapping, special actions, live readings, axis response, lightbar, touchpad, gyro and Advanced categories |
| [Controller audio and game feedback](controller-audio.md) | Native haptics, speakers, headsets, microphones, mute controls and feedback priorities |
| [Audio Haptics](user-guide.md#audio-haptics) | System/app capture, automatic game following, Mix/Replace and response tuning |
| [Trigger Lab](user-guide.md#trigger-lab) | Linked/split effects, presets, previews, full-pull actions and returning control to games |
| [Nintendo controllers and options](nintendo-options.md) | Pairing, optical mouse, motion, dual Joy-Con aiming, second actions, calibration and USB headsets |
| [Game Bar and integrations](integrations.md) | Game Bar compatibility, UDP motion output, OSC control/monitoring and links to DSX/streaming setup |
| [Swipe profiles](swipe-profiles.md) | Restrict two-finger profile switching to selected profiles |
| [Moonlight / Sunshine](moonlight-support.md) | Accept supported streamed virtual controllers on the host |
| [Flick stick](flick-stick-calibration.md) | In-game calibration and troubleshooting |
| [Mouse clicks](troubleshooting-mouse-clicks.md) | Diagnose simultaneous mouse-button mappings |
| [DSX game mods](DSX-game-mod-support.md) | Local mod setup, temporary trigger/light overrides and limits |

## Contribute and maintain

Implementation contracts and dated validation records are intended for
contributors. Use the feature guides above for everyday setup.

- [Contributing](../contributing.md): reporting bugs, proposing changes and documentation rules.
- [Development](development.md): pinned SDK, build, both test suites, packaging and code layout.
- [Dependency maintenance](dependency-maintenance.md): Dependabot coverage, lock-file review and manually maintained components.
- [Branch audit and cleanup record](../BRANCH-AUDIT.md): dated findings across the three forks and recorded cleanup decisions; recheck live branch tips before further cleanup.
- [Roadmap](roadmap.md): priorities and proposals, rather than a completed-task list.
- [Build and test audit](build-test-audit.md): measured pipeline costs, improvements and remaining tradeoffs.
- [Release process](release-process.md): numeric versions, draft builds and Latest publication.
- [Installer](../installer/README.md) and [installer validation](INSTALLER_VALIDATION_STRATEGY.md).
- [Translations](dev/translations.md) and [profile schema reference](dev/profile-schema-reference.md).
- [Technical reference](reference.md), including [Bluetooth audio/haptics](dualsense-bluetooth-audio-haptics.md) and [protocol contracts](protocols/README.md).
- [Validation archive](validation/README.md): dated tests and investigations.

Release history belongs in [CHANGELOG.md](../CHANGELOG.md). Keep one canonical
page per topic, and link directly to it. A dated investigation describes its
recorded source and hardware; it does not establish the status of a newer build.
