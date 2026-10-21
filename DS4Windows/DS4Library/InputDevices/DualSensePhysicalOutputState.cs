/*
DS4Windows
Copyright (C) 2026 hbashton

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
*/

using System;

namespace DS4Windows.InputDevices
{
    /// <summary>
    /// One complete, value-owned DualSense compositor input. Producers only
    /// publish copies of this value. The physical output owner claims one
    /// coherent version before encoding, so no report can combine fields from
    /// two profile, trigger, lightbar, or native-ownership publications.
    /// </summary>
    internal readonly record struct DualSensePhysicalOutputSnapshot(
        byte HapticPowerLevel,
        bool UseRumble,
        bool UseAccurateRumble,
        DS4ForceFeedbackState RumbleState,
        bool PreviewLightRumbleActive,
        byte PreviewLightRumbleStrength,
        bool PreviewHeavyRumbleActive,
        byte PreviewHeavyRumbleStrength,
        long RumbleGeneration,
        byte HeadphoneVolume,
        byte SpeakerVolume,
        bool HeadsetOnlyAudio,
        byte MicrophoneVolume,
        bool EnableSpeakerOutput,
        DualSenseDevice.TriggerEffectData LeftTrigger,
        DualSenseDevice.TriggerEffectData RightTrigger,
        byte MuteLedByte,
        bool MicrophoneMuteOverride,
        bool MicrophoneMuted,
        bool SpeakerMuteOverride,
        bool SpeakerMuted,
        bool MuteLedOverride,
        bool MuteLedOn,
        byte ActivePlayerLedMask,
        DS4LightbarState ProfileLightbar,
        bool NativeGameLightbarOwnershipReleased)
    {
        // A session-owned presentation layer; the profile/Trigger Lab values
        // above remain current while impulses temporarily override either side.
        internal object XboxImpulseOwner { get; init; }
        internal byte LeftXboxImpulse { get; init; }
        internal byte RightXboxImpulse { get; init; }
        internal DualSenseDsxOverlay DsxOverlay { get; init; }
        internal DualSenseDsxOverlay DsxNativeState { get; init; }
        internal bool ProfileOwnsLightbar { get; init; }
        internal byte? ProfilePlayerLedMask { get; init; }
        internal byte? ProfileMicLedByte { get; init; }
        internal bool IndicatorsFollowApplication { get; init; }
        internal long IndicatorReapplyRevision { get; init; }
        internal byte EffectivePlayerLedMask => ProfilePlayerLedMask ??
            (IndicatorsFollowApplication && !NativeGameLightbarOwnershipReleased ? DsxNativeState.PlayerLeds : null) ?? ActivePlayerLedMask;
        internal byte EffectiveMicLedByte => ProfileMicLedByte ??
            (IndicatorsFollowApplication ? DsxNativeState.MicLed : null) ?? MuteLedByte;
        internal bool LeftTriggerLabActive { get; init; }
        internal bool RightTriggerLabActive { get; init; }
        internal int DsxReleaseFields { get; init; }
        internal long DsxReleaseRevision { get; init; }

        internal DualSensePhysicalOutputSnapshot ForLocalTriggerReport() => this with
        {
            LeftTrigger = LeftTriggerLabActive ? LeftTrigger : DsxOverlay.Left ??
                (LeftXboxImpulse == 0 ? LeftTrigger : XboxImpulseTriggerEffect.Encode(LeftXboxImpulse)),
            RightTrigger = RightTriggerLabActive ? RightTrigger : DsxOverlay.Right ??
                (RightXboxImpulse == 0 ? RightTrigger : XboxImpulseTriggerEffect.Encode(RightXboxImpulse)),
        };

        internal static DualSensePhysicalOutputSnapshot Default => new(
            HapticPowerLevel:
                (byte)DualSenseDevice.HapticPowerLevelFriendlyName.Str100,
            UseRumble: true,
            UseAccurateRumble: true,
            RumbleState: default,
            PreviewLightRumbleActive: false,
            PreviewLightRumbleStrength: 0,
            PreviewHeavyRumbleActive: false,
            PreviewHeavyRumbleStrength: 0,
            RumbleGeneration: 0,
            HeadphoneVolume: 128,
            SpeakerVolume: 128,
            HeadsetOnlyAudio: false,
            MicrophoneVolume: 128,
            EnableSpeakerOutput: false,
            LeftTrigger: default,
            RightTrigger: default,
            MuteLedByte: 0,
            MicrophoneMuteOverride: false,
            MicrophoneMuted: false,
            SpeakerMuteOverride: false,
            SpeakerMuted: false,
            MuteLedOverride: false,
            MuteLedOn: false,
            ActivePlayerLedMask: 0,
            ProfileLightbar: default,
            NativeGameLightbarOwnershipReleased: true);
    }

    /// <summary>
    /// Fixed latest-state mailbox. The monitor protects only a value copy and
    /// version counter; callers signal the compositor after this lock is
    /// released, and the owner performs all I/O after claiming a copy.
    /// </summary>
    internal sealed class DualSensePhysicalOutputStateMailbox
    {
        private readonly object syncRoot = new();
        private DualSensePhysicalOutputSnapshot latest =
            DualSensePhysicalOutputSnapshot.Default;
        private long version = 1;
        private object batteryRumbleOwner;
        internal IDisposable TryBeginBatteryAlertRumble(byte strength, Action changed)
        {
            lock (syncRoot)
            {
                if (strength == 0 || latest.PreviewLightRumbleActive || latest.PreviewHeavyRumbleActive ||
                    latest.RumbleState.RumbleMotorStrengthLeftHeavySlow != 0 ||
                    latest.RumbleState.RumbleMotorStrengthRightLightFast != 0) return null;
                object owner = batteryRumbleOwner = new();
                PublishLocked(latest with { PreviewLightRumbleActive = true, PreviewHeavyRumbleActive = true,
                    PreviewLightRumbleStrength = strength, PreviewHeavyRumbleStrength = strength,
                    RumbleGeneration = latest.RumbleGeneration + 1 });
                return new BatteryAlertRumbleLease(() =>
                {
                    lock (syncRoot)
                    {
                        if (!ReferenceEquals(batteryRumbleOwner, owner)) return;
                        batteryRumbleOwner = null;
                        PublishLocked(latest with { PreviewLightRumbleActive = false, PreviewHeavyRumbleActive = false,
                            PreviewLightRumbleStrength = 0, PreviewHeavyRumbleStrength = 0,
                            RumbleGeneration = latest.RumbleGeneration + 1 });
                    }
                    changed();
                });
            }
        }

        internal DualSensePhysicalOutputSnapshot ReadLatest()
        {
            lock (syncRoot)
            {
                return latest;
            }
        }

        internal bool TryClaim(ref long claimedVersion,
            out DualSensePhysicalOutputSnapshot snapshot)
        {
            lock (syncRoot)
            {
                snapshot = latest;
                if (claimedVersion == version)
                {
                    return false;
                }

                claimedVersion = version;
                return true;
            }
        }

        internal bool Publish(in DualSensePhysicalOutputSnapshot snapshot)
        {
            lock (syncRoot)
            {
                if (latest.Equals(snapshot))
                {
                    return false;
                }

                latest = snapshot;
                version++;
                return true;
            }
        }

        internal bool SetHapticPowerLevel(byte value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    HapticPowerLevel = value,
                });
            }
        }

        internal bool SetUseRumble(bool value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with { UseRumble = value });
            }
        }

        internal bool SetUseAccurateRumble(bool value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    UseAccurateRumble = value,
                });
            }
        }

        internal bool SetHapticState(in DS4LightbarState lightbar,
            in DS4ForceFeedbackState rumble,
            out DualSensePhysicalOutputSnapshot snapshot)
        {
            lock (syncRoot)
            {
                snapshot = latest with
                {
                    ProfileLightbar = lightbar,
                    RumbleState = rumble,
                    RumbleGeneration = latest.RumbleGeneration + 1,
                };
                return PublishLocked(snapshot);
            }
        }

        internal bool SetRumbleState(in DS4ForceFeedbackState rumble,
            out DualSensePhysicalOutputSnapshot snapshot)
        {
            lock (syncRoot)
            {
                snapshot = latest with
                {
                    RumbleState = rumble,
                    RumbleGeneration = latest.RumbleGeneration + 1,
                };
                return PublishLocked(snapshot);
            }
        }

        internal bool SetRumbleChannel(bool rightLightFast, byte value,
            out DualSensePhysicalOutputSnapshot snapshot)
        {
            lock (syncRoot)
            {
                DS4ForceFeedbackState rumble = latest.RumbleState;
                if (rightLightFast)
                {
                    rumble.RumbleMotorStrengthRightLightFast = value;
                }
                else
                {
                    rumble.RumbleMotorStrengthLeftHeavySlow = value;
                }
                rumble.RumbleMotorsExplicitlyOff =
                    rumble.RumbleMotorStrengthRightLightFast == 0 &&
                    rumble.RumbleMotorStrengthLeftHeavySlow == 0;
                snapshot = latest with
                {
                    RumbleState = rumble,
                    RumbleGeneration = latest.RumbleGeneration + 1,
                };
                return PublishLocked(snapshot);
            }
        }

        internal bool SetRumblePreview(bool lightActive, byte lightStrength,
            bool heavyActive, byte heavyStrength,
            out DualSensePhysicalOutputSnapshot snapshot)
        {
            lock (syncRoot)
            {
                batteryRumbleOwner = null;
                DS4ForceFeedbackState rumble = latest.RumbleState;
                if (!lightActive && !heavyActive)
                {
                    rumble = default;
                    rumble.RumbleMotorsExplicitlyOff = true;
                }
                snapshot = latest with
                {
                    RumbleState = rumble,
                    PreviewLightRumbleActive = lightActive,
                    PreviewLightRumbleStrength = lightStrength,
                    PreviewHeavyRumbleActive = heavyActive,
                    PreviewHeavyRumbleStrength = heavyStrength,
                    RumbleGeneration = latest.RumbleGeneration + 1,
                };
                return PublishLocked(snapshot);
            }
        }

        internal bool ClearRumblePreview(
            out DualSensePhysicalOutputSnapshot snapshot) =>
            SetRumblePreview(false, 0, false, 0, out snapshot);

        internal bool SetHeadphoneVolume(byte value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    HeadphoneVolume = value,
                });
            }
        }

        internal bool SetSpeakerVolume(byte value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with { SpeakerVolume = value });
            }
        }

        internal bool SetHeadsetOnlyAudio(bool value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    HeadsetOnlyAudio = value,
                });
            }
        }

        internal bool SetMicrophoneVolume(byte value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    MicrophoneVolume = value,
                });
            }
        }

        internal bool SetEnableSpeakerOutput(bool value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    EnableSpeakerOutput = value,
                });
            }
        }

        internal bool SetTrigger(TriggerId trigger,
            in DualSenseDevice.TriggerEffectData value)
        {
            lock (syncRoot)
            {
                DualSensePhysicalOutputSnapshot next = trigger switch
                {
                    TriggerId.LeftTrigger => latest with
                    {
                        LeftTrigger = value,
                    },
                    TriggerId.RightTrigger => latest with
                    {
                        RightTrigger = value,
                    },
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(trigger), "Invalid Trigger Id"),
                };
                return PublishLocked(next);
            }
        }

        internal bool TrySetXboxImpulse(object owner, byte left, byte right, out bool changed)
        {
            ArgumentNullException.ThrowIfNull(owner);
            lock (syncRoot)
            {
                changed = false;
                bool release = left == 0 && right == 0;
                if (latest.XboxImpulseOwner != null && !ReferenceEquals(latest.XboxImpulseOwner, owner))
                    return release; // A retiring predecessor cannot clear its successor.
                if (release && latest.XboxImpulseOwner == null) return true;
                changed = PublishLocked(latest with
                {
                    XboxImpulseOwner = release ? null : owner,
                    LeftXboxImpulse = left,
                    RightXboxImpulse = right,
                });
                return true;
            }
        }

        internal bool TryUpdateDsxOverlay(object owner, Func<DualSenseDsxOverlay, DualSenseDsxOverlay> update, out bool changed)
        {
            ArgumentNullException.ThrowIfNull(owner);
            lock (syncRoot)
            {
                changed = false;
                if (latest.DsxOverlay.Owner != null && !ReferenceEquals(latest.DsxOverlay.Owner, owner)) return false;
                var next = update(latest.DsxOverlay) with { Owner = owner };
                int removed = DualSenseDsxOverlay.Released(latest.DsxOverlay, next);
                changed = PublishLocked(latest with { DsxOverlay = next,
                    DsxReleaseFields = latest.DsxReleaseFields | removed,
                    DsxReleaseRevision = latest.DsxReleaseRevision + (removed == 0 ? 0 : 1) });
                return true;
            }
        }

        internal bool ReleaseDsxOverlay(object owner, out bool changed)
        {
            lock (syncRoot)
            {
                changed = false;
                if (!ReferenceEquals(latest.DsxOverlay.Owner, owner)) return false;
                changed = PublishLocked(latest with { DsxOverlay = default,
                    DsxReleaseFields = latest.DsxReleaseFields | latest.DsxOverlay.Fields,
                    DsxReleaseRevision = latest.DsxReleaseRevision + 1 });
                return true;
            }
        }

        internal void AcknowledgeDsxRelease(long revision, int fields)
        {
            lock (syncRoot)
                if (latest.DsxReleaseRevision == revision)
                    PublishLocked(latest with { DsxReleaseFields = latest.DsxReleaseFields & ~fields });
        }

        internal void ObserveDsxNativeState(byte[] report, int offset)
        {
            lock (syncRoot) PublishLocked(latest with { DsxNativeState = latest.DsxNativeState.ObserveNative(report, offset) });
        }

        internal void ClearDsxNativeState()
        {
            lock (syncRoot) PublishLocked(latest with { DsxNativeState = default });
        }

        internal bool SetTriggerLabTrigger(TriggerId trigger, in DualSenseDevice.TriggerEffectData effect, bool active)
        {
            lock (syncRoot)
                return PublishLocked(trigger == TriggerId.LeftTrigger
                    ? latest with { LeftTrigger = effect, LeftTriggerLabActive = active }
                    : latest with { RightTrigger = effect, RightTriggerLabActive = active });
        }

        internal bool SetMicrophoneMute(bool enabled, bool muted)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    MicrophoneMuteOverride = enabled,
                    MicrophoneMuted = enabled && muted,
                });
            }
        }

        internal bool SetProfileMuteButtonState(bool muteLedOverride,
            bool muteLedOn, bool microphoneMuteOverride,
            bool microphoneMuted, bool speakerMuteOverride,
            bool speakerMuted, byte speakerVolume)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    MuteLedOverride = muteLedOverride,
                    MuteLedOn = muteLedOverride && muteLedOn,
                    MicrophoneMuteOverride = microphoneMuteOverride,
                    MicrophoneMuted = microphoneMuteOverride &&
                        microphoneMuted,
                    SpeakerMuteOverride = speakerMuteOverride,
                    SpeakerMuted = speakerMuteOverride && speakerMuted,
                    // Muting the built-in speaker is a gain-only operation.
                    // EnableSpeakerOutput, HeadphoneVolume, and the live media
                    // carrier remain untouched so AUX audio, haptics, and the
                    // real-time capture/encoder lifecycle do not restart.
                    SpeakerVolume = speakerVolume,
                });
            }
        }

        internal bool SetProfileAudioAndMuteButtonState(
            bool enableSpeakerOutput, byte speakerVolume,
            byte headphoneVolume, bool headsetOnlyAudio,
            bool muteLedOverride, bool muteLedOn,
            bool microphoneMuteOverride, bool microphoneMuted,
            bool speakerMuteOverride, bool speakerMuted)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    EnableSpeakerOutput = enableSpeakerOutput,
                    SpeakerVolume = speakerVolume,
                    HeadphoneVolume = headphoneVolume,
                    HeadsetOnlyAudio = headsetOnlyAudio,
                    MuteLedOverride = muteLedOverride,
                    MuteLedOn = muteLedOverride && muteLedOn,
                    MicrophoneMuteOverride = microphoneMuteOverride,
                    MicrophoneMuted = microphoneMuteOverride &&
                        microphoneMuted,
                    SpeakerMuteOverride = speakerMuteOverride,
                    SpeakerMuted = speakerMuteOverride && speakerMuted,
                });
            }
        }

        internal bool SetMuteLedOverride(bool enabled, bool ledOn)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    MuteLedOverride = enabled,
                    MuteLedOn = ledOn,
                });
            }
        }

        internal bool SetProfileIndicators(byte? player, byte? mic, bool reapply, bool passthrough = false)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    ProfilePlayerLedMask = player,
                    ProfileMicLedByte = mic,
                    IndicatorsFollowApplication = passthrough,
                    // Another physical writer can change LEDs without changing
                    // our desired state. An explicit reapply must dirty output
                    // even when the selected mask is still zero (Off).
                    IndicatorReapplyRevision = latest.IndicatorReapplyRevision + (reapply ? 1 : 0),
                });
            }
        }

        internal bool SetMuteLedByte(byte value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with { MuteLedByte = value });
            }
        }

        internal bool SetActivePlayerLedMask(byte value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    ActivePlayerLedMask = value,
                });
            }
        }

        internal bool SetProfileLightbar(in DS4LightbarState value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    ProfileLightbar = value,
                });
            }
        }

        internal bool SetProfileLightbarOwnership(bool value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    ProfileOwnsLightbar = value,
                    IndicatorsFollowApplication = !value,
                });
            }
        }

        internal bool SetNativeGameLightbarOwnershipReleased(bool value)
        {
            lock (syncRoot)
            {
                return PublishLocked(latest with
                {
                    NativeGameLightbarOwnershipReleased = value,
                });
            }
        }

        private bool PublishLocked(
            in DualSensePhysicalOutputSnapshot next)
        {
            if (latest.Equals(next))
            {
                return false;
            }

            latest = next;
            version++;
            return true;
        }
    }
}
