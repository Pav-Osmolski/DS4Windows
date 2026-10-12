# Branch audit — 2026-10-11

Audited the remote branches in the three Pav-Osmolski forks after publishing DS4Windows 5.0.16. On 2026-10-11, the maintainer authorized deletion of the 11 fully merged branches and enabling automatic branch deletion after future PR merges. All 11 tips were rechecked against the current default branches before deletion. The remaining branch tips were verified unchanged, and every release tag reference was preserved.

**40 non-default branches remain:** 16 with all non-merge patches already represented in the default branch; 2 contained in other retained experimental branches; 21 with unmerged work to review or archive; 1 documentation deployment branch. The 11 fully merged branches were deleted and are recorded below. Keep DS4Windows/main, VIIPER/main and DS4Updater/master.

39 remaining branches have exactly the same tip as the corresponding upstream hbashton branch. These are inherited copies, not newly created work in this fork. Automatic deletion after PR merges is now enabled in all three repositories. No further branch cleanup is scheduled; the maintainer will review the remaining branches later.

## Dependency refresh follow-up — 2026-10-12

The four inherited DS4Windows dependency proposals below are superseded by
current-fork changes: [#15](https://github.com/Pav-Osmolski/DS4Windows/pull/15)
aligns System.Management, [#16](https://github.com/Pav-Osmolski/DS4Windows/pull/16)
removes the redundant direct System.Memory reference, and
[#17](https://github.com/Pav-Osmolski/DS4Windows/pull/17) replaces both overlapping
MSTest proposals. [#14](https://github.com/Pav-Osmolski/DS4Windows/pull/14)
configures fresh fork-specific NuGet and Actions proposals.

Keep the old dependency branch pointers as historical references pending the
maintainer's deferred cleanup decision. No inherited branch or release tag was
deleted during this refresh. The counts and classifications below remain the
original 2026-10-11 audit snapshot, not a fresh count of today's branches.

## How to interpret the recommendations

- **Fully merged:** the entire branch tip is an ancestor of the default branch. Its branch pointer is redundant; deleting it does not remove commits from main or release tags.
- **Equivalent patches:** Git reports no unmatched non-merge patches, despite different commit history. This is strong evidence of incorporated work, but is not the same as full ancestry and does not independently prove that every merge resolution or later behaviour is identical. Preserve a tag or Git bundle before deleting these historical references.
- **Contained elsewhere:** the complete branch tip is an ancestor of the named experimental branch. It is redundant only while that containing branch or another archived reference is retained.
- **Unmerged work:** preserve or archive first. Unique patch counts are history evidence, not proof that a feature is absent, correct, current or ready to ship. These experiments have not been built or hardware-tested during this audit. Do not merge their old snapshots wholesale.
- **gh-pages:** generated documentation output, not application source. The current VIIPER docs workflow writes to it with mike. Keep it while that deployment workflow remains enabled. The Pages API returned 404, so this audit does not confirm a live public Pages site.

## Completed cleanup

| Repository | Deleted branch | Preserved tip |
|---|---|---|
| DS4Windows | `feature/ui-consistency` | [391af41](https://github.com/Pav-Osmolski/DS4Windows/commit/391af41a45c00e4b9f28b34037868e3b4b4600b0) |
| DS4Windows | `feature/usbip-0.9.8.1` | [528c5dd](https://github.com/Pav-Osmolski/DS4Windows/commit/528c5dd0ed58a938af00b2dba7cccabba93056f6) |
| VIIPER | `agent/viiper-tray-qc` | [56bf8f2](https://github.com/Pav-Osmolski/VIIPER/commit/56bf8f246245bb1b9ec90356f409d950afeb92c7) |
| VIIPER | `docs/documentation-refresh` | [3aefc98](https://github.com/Pav-Osmolski/VIIPER/commit/3aefc985bb006026445fdd3d57998253741526af) |
| VIIPER | `docs/remove-redundant-pointers` | [803aaf3](https://github.com/Pav-Osmolski/VIIPER/commit/803aaf38c267c2ba207d712c3135d3d82226439d) |
| VIIPER | `feature/native-udecx-landing-zone` | [07db0a8](https://github.com/Pav-Osmolski/VIIPER/commit/07db0a810b9c0866e9e0e5dddc15a3b7e47470cb) |
| VIIPER | `feature/usbip-0.9.8.1` | [c563712](https://github.com/Pav-Osmolski/VIIPER/commit/c563712ab2506cdaf3e91328873026bc50a6313d) |
| VIIPER | `fix/bt-haptics-reference` | [2ea6829](https://github.com/Pav-Osmolski/VIIPER/commit/2ea6829ec0a16759fa50dd323b4217bf27bef418) |
| DS4Updater | `docs/documentation-refresh` | [a5f6891](https://github.com/Pav-Osmolski/DS4Updater/commit/a5f6891a1e5048d27e813ac5e33128e4de262ffe) |
| DS4Updater | `docs/remove-redundant-pointers` | [8b8f655](https://github.com/Pav-Osmolski/DS4Updater/commit/8b8f655be309d2b1513d51adde38b02fb02e98be) |
| DS4Updater | `fix/fork-update-sources` | [11087cc](https://github.com/Pav-Osmolski/DS4Updater/commit/11087ccf43a6a1df390f2f7c2cdaf0b4f9b0574c) |

Each deleted tip remains reachable through its repository’s default branch. The table records the deleted references without linking to branches that no longer exist.

## DS4Windows

Default branch: `main` — keep.

| Branch | Recommendation | Evidence / purpose |
|---|---|---|
| [SDL3](https://github.com/Pav-Osmolski/DS4Windows/tree/SDL3) | Review or archive: unmerged work | Documentation-only SDL3 migration investigation; the unique commit adds a 99-line design note, not an implemented backend. Archive or port the useful note before deleting. Tip `e38a328`, 2026-06-07. |
| [agent/dualsense-6a89-popfix](https://github.com/Pav-Osmolski/DS4Windows/tree/agent/dualsense-6a89-popfix) | Archive then delete: equivalent patches | 266 non-merge patches already represented in `main`; historical reference only. Tip: Reassert DualSense speaker state after AUX sessions. Tip `117853e`, 2026-07-29. |
| [agent/padforge-dualsense-audio](https://github.com/Pav-Osmolski/DS4Windows/tree/agent/padforge-dualsense-audio) | Review or archive: unmerged work | Four unmerged patches for isolated DualSense speaker transport, owner-state deduplication and Opus settings. Worth reviewing for DualSense audio work. Tip `9b887b1`, 2026-07-28. |
| [battery-toast-notifications](https://github.com/Pav-Osmolski/DS4Windows/tree/battery-toast-notifications) | Review or archive: unmerged work | One unmerged feature patch: battery toast notifications, settings and a notifier service. A manageable candidate for a fresh feature branch. Tip `f0ae32b`, 2026-06-11. |
| [cleanroom-dualsense-bt-audio](https://github.com/Pav-Osmolski/DS4Windows/tree/cleanroom-dualsense-bt-audio) | Review or archive: unmerged work | Three unmerged DualSense Bluetooth audio transport/diagnostic patches. Preserve as reference; review against the current audio implementation. Tip `8513a6e`, 2026-06-05. |
| [dependabot/nuget/DS4Windows/System.Management-10.0.8](https://github.com/Pav-Osmolski/DS4Windows/tree/dependabot/nuget/DS4Windows/System.Management-10.0.8) | Review or archive: unmerged work | Inherited dependency-update proposal from June; replace with a fresh update against this fork rather than merging an old dependency snapshot. Upstream: [#10](https://github.com/hbashton/DS4Windows/pull/10) (open). Tip `6aa97ff`, 2026-06-02. |
| [dependabot/nuget/DS4Windows/System.Memory-4.6.3](https://github.com/Pav-Osmolski/DS4Windows/tree/dependabot/nuget/DS4Windows/System.Memory-4.6.3) | Review or archive: unmerged work | Inherited dependency-update proposal from June; replace with a fresh update against this fork rather than merging an old dependency snapshot. Upstream: [#11](https://github.com/hbashton/DS4Windows/pull/11) (open). Tip `886c59e`, 2026-06-02. |
| [dependabot/nuget/DS4WindowsTests/MSTest.TestFramework-4.2.3](https://github.com/Pav-Osmolski/DS4Windows/tree/dependabot/nuget/DS4WindowsTests/MSTest.TestFramework-4.2.3) | Review or archive: unmerged work | Inherited dependency-update proposal from June; replace with a fresh update against this fork rather than merging an old dependency snapshot. Upstream: [#8](https://github.com/hbashton/DS4Windows/pull/8) (open). Tip `75ac2fe`, 2026-06-02. |
| [dependabot/nuget/DS4WindowsTests/multi-3f71d811c1](https://github.com/Pav-Osmolski/DS4Windows/tree/dependabot/nuget/DS4WindowsTests/multi-3f71d811c1) | Review or archive: unmerged work | Inherited dependency-update proposal from June; replace with a fresh update against this fork rather than merging an old dependency snapshot. Upstream: [#7](https://github.com/hbashton/DS4Windows/pull/7) (open). Tip `d404d86`, 2026-06-02. |
| [dualsense-bluetooth-emulation-driver](https://github.com/Pav-Osmolski/DS4Windows/tree/dualsense-bluetooth-emulation-driver) | Archive then delete: equivalent patches | 44 non-merge patches already represented in `main`; historical reference only. Tip: Route virtual DualSense IOCTLs through control device. Tip `e852043`, 2026-06-11. |
| [experiment/xbox-series-emulation](https://github.com/Pav-Osmolski/DS4Windows/tree/experiment/xbox-series-emulation) | Review or archive: unmerged work | Unmerged Xbox Series X|S emulation experiment. The DS4Windows and VIIPER branches are a paired feature; preserve together if pursued. Tip `d48ad0b`, 2026-08-06. |
| [feature/native-udecx-bus-integration](https://github.com/Pav-Osmolski/DS4Windows/tree/feature/native-udecx-bus-integration) | Review or archive: unmerged work | 38 unmerged patches integrating native UdeCx transport and driver package validation. A substantial driver/backend project, not a routine cleanup merge. Tip `0b68dc0`, 2026-08-13. |
| [feature/native-udecx-integration](https://github.com/Pav-Osmolski/DS4Windows/tree/feature/native-udecx-integration) | Review or archive: unmerged work | 16 patch-unique changes, including native transport, local driver trust and validation/recovery tooling. Upstream PR #70 is still open. Also preserves the viiper-ui-rebuild tip. Upstream: [#70](https://github.com/hbashton/DS4Windows/pull/70) (open). Tip `08d2c76`, 2026-08-16. |
| [fix-profile-switch-lag](https://github.com/Pav-Osmolski/DS4Windows/tree/fix-profile-switch-lag) | Archive then delete: equivalent patches | 11 non-merge patches already represented in `main`; historical reference only. Tip: Reduce profile switch action input stalls. Tip `25078b6`, 2026-06-07. |
| [invert-axis](https://github.com/Pav-Osmolski/DS4Windows/tree/invert-axis) | Review or archive: unmerged work | One unmerged patch adds per-stick axis inversion, profile persistence, UI and tests. A worthwhile small feature candidate. Tip `39eacf6`, 2026-06-05. |
| [investigate-auto-profile-duplicate-crash](https://github.com/Pav-Osmolski/DS4Windows/tree/investigate-auto-profile-duplicate-crash) | Archive then delete: equivalent patches | 11 non-merge patches already represented in `main`; historical reference only. Tip: Fix duplicate auto-profile crash handling. Tip `e8db7b6`, 2026-06-07. |
| [investigate-issue-110-dualsense-bt-reconnect](https://github.com/Pav-Osmolski/DS4Windows/tree/investigate-issue-110-dualsense-bt-reconnect) | Review or archive: unmerged work | One unmerged reconnect patch changes ControlService and DS4Devices. Relevant to the maintainer’s DualSense; investigate whether current reconnect handling already supersedes it. Tip `242da5f`, 2026-06-06. |
| [investigate-virtual-controller-crash](https://github.com/Pav-Osmolski/DS4Windows/tree/investigate-virtual-controller-crash) | Archive then delete: equivalent patches | 20 non-merge patches already represented in `main`; historical reference only. Tip: Remove virtual controller diagnostic logging. Tip `32ee486`, 2026-06-08. |
| [merge-ds4windowsapp-pr-9](https://github.com/Pav-Osmolski/DS4Windows/tree/merge-ds4windowsapp-pr-9) | Archive then delete: equivalent patches | 9 non-merge patches already represented in `main`; historical reference only. Tip: fix: preserve interrupt pipe for copycat controllers over BT. Tip `9f3351f`, 2026-06-06. |
| [profile-disable-hidhide](https://github.com/Pav-Osmolski/DS4Windows/tree/profile-disable-hidhide) | Review or archive: unmerged work | One unmerged patch adds a per-profile HidHide disable setting and runtime handling. Worth reviewing, including interactions with current device hiding. Tip `119f8fe`, 2026-06-08. |
| [startup-crash-diag](https://github.com/Pav-Osmolski/DS4Windows/tree/startup-crash-diag) | Archive then delete: equivalent patches | 39 non-merge patches already represented in `main`; historical reference only. Tip: Suppress Game Bar probe startup cursor. Tip `f60e7b3`, 2026-06-11. |
| [triggertest](https://github.com/Pav-Osmolski/DS4Windows/tree/triggertest) | Review or archive: unmerged work | 11 commits in an unrelated Git history, containing early trigger, microphone and Bluetooth-audio diagnostics. Archive as historical research; do not merge wholesale. Tip `cbe418c`, 2026-06-03. |
| [viiper-full-backend](https://github.com/Pav-Osmolski/DS4Windows/tree/viiper-full-backend) | Archive then delete: equivalent patches | 150 non-merge patches already represented in `main`; historical reference only. Tip: Keep VIIPER input responsive during microphone capture. Tip `c4e0700`, 2026-07-17. |
| [viiper-full-backend-debug](https://github.com/Pav-Osmolski/DS4Windows/tree/viiper-full-backend-debug) | Review or archive: unmerged work | 72 patch-unique commits of older DualSense speaker/microphone debugging, including reverts. Archive as diagnostic research; not a release-ready feature branch. Tip `590e906`, 2026-06-28. |
| [viiper-microphone-rebuild](https://github.com/Pav-Osmolski/DS4Windows/tree/viiper-microphone-rebuild) | Archive then delete: equivalent patches | 141 non-merge patches already represented in `main`; historical reference only. Tip: Release HidHide devices while service is stopped. Tip `9d8f30e`, 2026-07-17. |
| [viiper-raw-haptics-capture](https://github.com/Pav-Osmolski/DS4Windows/tree/viiper-raw-haptics-capture) | Review or archive: unmerged work | Two unmerged verbose raw-haptics diagnostic patches. Potentially useful for investigating DualSense haptics, rather than a normal release feature. Tip `d7c1827`, 2026-06-13. |
| [viiper-ui-rebuild](https://github.com/Pav-Osmolski/DS4Windows/tree/viiper-ui-rebuild) | Delete if containing branch is kept | Entire tip is contained in `feature/native-udecx-integration`; retain that branch or an archive first. Upstream: [#51](https://github.com/hbashton/DS4Windows/pull/51) (merged). Tip `85acc22`, 2026-07-25. |
| [virtual-dualsense-output](https://github.com/Pav-Osmolski/DS4Windows/tree/virtual-dualsense-output) | Archive then delete: equivalent patches | 28 non-merge patches already represented in `main`; historical reference only. Tip: Improve virtual DualSense driver install path. Tip `e0cf746`, 2026-06-11. |
| [virtual-profile-crash-fix](https://github.com/Pav-Osmolski/DS4Windows/tree/virtual-profile-crash-fix) | Review or archive: unmerged work | Two unmerged virtual-controller profile-switch/hotplug guards. Review against current device filtering and crash handling before deciding whether to port. Tip `6be6ab6`, 2026-06-09. |

## VIIPER

Default branch: `main` — keep.

| Branch | Recommendation | Evidence / purpose |
|---|---|---|
| [agent/padsense-native480](https://github.com/Pav-Osmolski/VIIPER/tree/agent/padsense-native480) | Archive then delete: equivalent patches | 96 non-merge patches already represented in `main`; historical reference only. Tip: Make VIIPER 0.0.6 setup fail closed and recoverable. Tip `93247f1`, 2026-07-30. |
| [ds4-audio-emulation](https://github.com/Pav-Osmolski/VIIPER/tree/ds4-audio-emulation) | Archive then delete: equivalent patches | 88 non-merge patches already represented in `main`; historical reference only. Tip: Add PlayStation audio-only virtual devices. Tip `02fffe6`, 2026-07-23. |
| [experiment/xbox-series-emulation](https://github.com/Pav-Osmolski/VIIPER/tree/experiment/xbox-series-emulation) | Review or archive: unmerged work | Unmerged Xbox Series X|S emulation experiment. The DS4Windows and VIIPER branches are a paired feature; preserve together if pursued. Tip `a612d14`, 2026-08-06. |
| [feature/latency-superiority](https://github.com/Pav-Osmolski/VIIPER/tree/feature/latency-superiority) | Review or archive: unmerged work | 244 unmerged patches: native UdeCx driver/backend plus latency validation. Upstream PR #8 remains open. Preserves all 240 commits of feature/native-udecx-bus; keep as the more complete research branch. Upstream: [#8](https://github.com/hbashton/VIIPER/pull/8) (open). Tip `2da61e4`, 2026-08-16. |
| [feature/native-udecx-bus](https://github.com/Pav-Osmolski/VIIPER/tree/feature/native-udecx-bus) | Delete if containing branch is kept | Entire tip is contained in `feature/latency-superiority`; retain that branch or an archive first. Upstream: [#9](https://github.com/hbashton/VIIPER/pull/9) (open). Tip `cece307`, 2026-08-15. |
| [fix-iso-audio-clock](https://github.com/Pav-Osmolski/VIIPER/tree/fix-iso-audio-clock) | Archive then delete: equivalent patches | 54 non-merge patches already represented in `main`; historical reference only. Tip: Expose DualSense microphone input endpoint. Tip `dc475f1`, 2026-06-24. |
| [fix-virtual-uac-interface](https://github.com/Pav-Osmolski/VIIPER/tree/fix-virtual-uac-interface) | Archive then delete: equivalent patches | 39 non-merge patches already represented in `main`; historical reference only. Tip: Implement USBIP isochronous transfer framing. Tip `9d2f1c1`, 2026-06-21. |
| [fix/release-assets-before-registries](https://github.com/Pav-Osmolski/VIIPER/tree/fix/release-assets-before-registries) | Archive then delete: equivalent patches | 98 non-merge patches already represented in `main`; historical reference only. Tip: Keep GitHub releases independent of registry publishing. Tip `c903470`, 2026-07-30. |
| [gh-pages](https://github.com/Pav-Osmolski/VIIPER/tree/gh-pages) | Keep: documentation deployment | Generated versioned MkDocs site; actively written by `.github/workflows/docs-deploy.yml`. Tip `f909158`, 2026-10-11. |
| [microphone-rebuild](https://github.com/Pav-Osmolski/VIIPER/tree/microphone-rebuild) | Archive then delete: equivalent patches | 76 non-merge patches already represented in `main`; historical reference only. Tip: Revert "Preserve DualSense HID input ordering". Tip `a47d816`, 2026-07-17. |

## DS4Updater

Default branch: `master` — keep.

| Branch | Recommendation | Evidence / purpose |
|---|---|---|
| [agent/obsolete-install-cleanup](https://github.com/Pav-Osmolski/DS4Updater/tree/agent/obsolete-install-cleanup) | Review or archive: unmerged work | One unmerged updater feature adds managed-file manifests and obsolete-file cleanup. Upstream PR #1 is open. Useful candidate, but review deletion boundaries and preservation of profiles/settings before porting. Upstream: [#1](https://github.com/hbashton/DS4Updater/pull/1) (open). Tip `be30b23`, 2026-07-21. |

## Deferred cleanup

The following work is left for the maintainer to decide later. No remaining branches were deleted, archived or merged during this cleanup.

1. Archive the 16 patch-equivalent historical branches before considering deletion of their branch pointers.
2. Consider the two contained branches only while retaining their named containing branches or archive tags.
3. Review or archive the 21 branches with unmerged work. The four inherited Dependabot proposals can be replaced by fresh fork-specific dependency updates.
4. Keep the three default branches, VIIPER gh-pages and all release tags. Automatic deletion after future PR merges is enabled.

The most practical review candidates are DualSense Bluetooth reconnect/audio, battery notifications, stick-axis inversion and updater managed-file cleanup. Native UdeCx and Xbox Series emulation are larger cross-repository projects.

## Audit data

This report records branch tips, patch-equivalence findings and relevant pull requests from the audit. It is a snapshot, not a release changelog or an instruction to delete further branches. Recheck live branch tips and ancestry immediately before any later cleanup.
