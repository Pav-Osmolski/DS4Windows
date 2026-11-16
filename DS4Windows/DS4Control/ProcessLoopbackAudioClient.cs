/*
DS4Windows - modified code derived from DS5 Bridge by SundayMoments
https://github.com/SundayMoments/DS5_Bridge
Copyright (C) 2026  SundayMoments (DS5 Bridge-derived code)
Copyright (C) 2026  DS4Windows contributors

DS5 Bridge-derived code: GNU Affero General Public License version 3 only
SPDX-License-Identifier: AGPL-3.0-only
Modified for DS4Windows; attribution and license notice restored 2026-11-16.
See DS5-BRIDGE-NOTICE.txt for provenance, modifications and the GPLv3/AGPLv3
combination terms, and AGPL-3.0.txt for the full license and warranty disclaimer.
*/

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DS4Windows
{
    /// <summary>
    /// Activates the Windows 10 process-loopback virtual audio interface. This
    /// is the documented Core Audio process-loopback protocol and lets a
    /// profile follow one application (including its child processes).
    /// </summary>
    internal static class ProcessLoopbackAudioClient
    {
        private const string ProcessLoopbackDevice = "VAD\\Process_Loopback";
        private const ushort VariantBlob = 65;
        private static readonly Guid AudioClientInterfaceId =
            new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

        public static AudioClient Activate(int processId,
            TimeSpan timeout)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            {
                throw new PlatformNotSupportedException(
                    "Per-app Audio Haptics requires Windows 10 build 20348 or newer.");
            }

            ActivationParameters parameters = new ActivationParameters
            {
                ActivationType = ActivationType.ProcessLoopback,
                Process = new ProcessParameters
                {
                    ProcessId = checked((uint)processId),
                    Mode = ProcessLoopbackMode.IncludeProcessTree,
                },
            };

            IntPtr parametersMemory = IntPtr.Zero;
            IntPtr variantMemory = IntPtr.Zero;
            IActivateAudioInterfaceAsyncOperation operation = null;
            try
            {
                parametersMemory = Marshal.AllocHGlobal(
                    Marshal.SizeOf<ActivationParameters>());
                Marshal.StructureToPtr(parameters, parametersMemory, false);
                BlobVariant variant = new BlobVariant
                {
                    Type = VariantBlob,
                    Size = Marshal.SizeOf<ActivationParameters>(),
                    Data = parametersMemory,
                };
                variantMemory = Marshal.AllocHGlobal(
                    Marshal.SizeOf<BlobVariant>());
                Marshal.StructureToPtr(variant, variantMemory, false);

                ActivationCompletion completion = new ActivationCompletion();
                Guid interfaceId = AudioClientInterfaceId;
                int result = ActivateAudioInterfaceAsync(ProcessLoopbackDevice,
                    ref interfaceId, variantMemory, completion, out operation);
                Marshal.ThrowExceptionForHR(result);
                if (!completion.Wait(timeout))
                {
                    throw new TimeoutException(
                        "Windows timed out while opening per-app audio capture.");
                }

                return new AudioClient(completion.GetAudioClient());
            }
            finally
            {
                if (operation != null && OperatingSystem.IsWindows())
                {
                    Marshal.ReleaseComObject(operation);
                }
                if (variantMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(variantMemory);
                }
                if (parametersMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(parametersMemory);
                }
            }
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true,
            CharSet = CharSet.Unicode)]
        private static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid interfaceId, IntPtr activationParameters,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [ComImport]
        [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            [PreserveSig]
            int ActivateCompleted(
                IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport]
        [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            [PreserveSig]
            int GetActivateResult(out int activationResult,
                [MarshalAs(UnmanagedType.IUnknown)] out object activatedObject);
        }

        private sealed class ActivationCompletion :
            IActivateAudioInterfaceCompletionHandler
        {
            private readonly ManualResetEventSlim completed = new(false);
            private int activationResult;
            private IAudioClient audioClient;
            private Exception error;

            public int ActivateCompleted(
                IActivateAudioInterfaceAsyncOperation operation)
            {
                try
                {
                    int callResult = operation.GetActivateResult(
                        out activationResult, out object activatedObject);
                    if (callResult < 0)
                    {
                        activationResult = callResult;
                    }
                    if (activationResult >= 0 &&
                        activatedObject is IAudioClient client)
                    {
                        audioClient = client;
                    }
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    completed.Set();
                }
                return 0;
            }

            public bool Wait(TimeSpan timeout) => completed.Wait(timeout);

            public IAudioClient GetAudioClient()
            {
                if (error != null)
                {
                    throw error;
                }
                Marshal.ThrowExceptionForHR(activationResult);
                return audioClient ?? throw new InvalidOperationException(
                    "Windows did not return an audio client for the selected app.");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlobVariant
        {
            public ushort Type;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public int Size;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ActivationParameters
        {
            public ActivationType ActivationType;
            public ProcessParameters Process;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessParameters
        {
            public uint ProcessId;
            public ProcessLoopbackMode Mode;
        }

        private enum ActivationType
        {
            Default,
            ProcessLoopback,
        }

        private enum ProcessLoopbackMode
        {
            IncludeProcessTree,
            ExcludeProcessTree,
        }
    }
}
