using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TideReader.Backend.Services;

public sealed class WindowsAudioSessionSnapshotProvider : IAudioSessionSnapshotProvider
{
    public Task<AudioSessionSnapshotResult> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var endpoints = new List<AudioEndpointSnapshot>();
        var results = new List<AudioSessionSnapshot>();

        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDeviceCollection? deviceCollection = null;
        string defaultEndpointId = "";

        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))
                ?? throw new InvalidOperationException("MMDeviceEnumerator COM type is unavailable.");
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            if (deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var defaultDevice) == 0)
            {
                try
                {
                    defaultDevice.GetId(out defaultEndpointId);
                }
                finally
                {
                    ReleaseComObject(defaultDevice);
                }
            }

            Marshal.ThrowExceptionForHR(deviceEnumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out deviceCollection));
            Marshal.ThrowExceptionForHR(deviceCollection.GetCount(out var deviceCount));

            for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMMDevice? device = null;
                IAudioSessionManager2? sessionManager = null;
                IAudioSessionEnumerator? sessionEnumerator = null;
                try
                {
                    Marshal.ThrowExceptionForHR(deviceCollection.Item(deviceIndex, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out var endpointId));
                    var friendlyName = TryReadFriendlyName(device);
                    device.GetState(out var deviceState);
                    endpoints.Add(new AudioEndpointSnapshot(
                        EndpointId: endpointId,
                        FriendlyName: friendlyName,
                        DeviceState: deviceState.ToString().ToLowerInvariant(),
                        IsDefaultMultimedia: string.Equals(endpointId, defaultEndpointId, StringComparison.OrdinalIgnoreCase)));

                    var interfaceId = typeof(IAudioSessionManager2).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, CLSCTX.All, IntPtr.Zero, out var managerObject));
                    sessionManager = (IAudioSessionManager2)managerObject;
                    Marshal.ThrowExceptionForHR(sessionManager.GetSessionEnumerator(out sessionEnumerator));
                    Marshal.ThrowExceptionForHR(sessionEnumerator.GetCount(out var sessionCount));

                    for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        IAudioSessionControl? sessionControl = null;
                        try
                        {
                            Marshal.ThrowExceptionForHR(sessionEnumerator.GetSession(sessionIndex, out sessionControl));
                            var snapshot = TryCreateSnapshot(sessionControl, endpointId, capturedAtUtc);
                            if (snapshot is not null)
                            {
                                results.Add(snapshot);
                            }
                        }
                        catch
                        {
                            // Ignore inaccessible sessions and keep enumerating.
                        }
                        finally
                        {
                            ReleaseComObject(sessionControl);
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible devices and keep enumerating.
                }
                finally
                {
                    ReleaseComObject(sessionEnumerator);
                    ReleaseComObject(sessionManager);
                    ReleaseComObject(device);
                }
            }
        }
        catch
        {
            return Task.FromResult(new AudioSessionSnapshotResult(endpoints, results));
        }
        finally
        {
            ReleaseComObject(deviceCollection);
            ReleaseComObject(deviceEnumerator);
        }

        return Task.FromResult(new AudioSessionSnapshotResult(endpoints, results));
    }

    private static string TryReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out propertyStore));
            var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
            Marshal.ThrowExceptionForHR(propertyStore.GetValue(ref key, out var value));
            try
            {
                return value.GetValue() ?? "";
            }
            finally
            {
                value.Dispose();
            }
        }
        catch
        {
            return "";
        }
        finally
        {
            ReleaseComObject(propertyStore);
        }
    }

    private static AudioSessionSnapshot? TryCreateSnapshot(IAudioSessionControl sessionControl, string endpointId, DateTimeOffset capturedAtUtc)
    {
        IAudioSessionControl2? sessionControl2 = null;
        ISimpleAudioVolume? audioVolume = null;
        IAudioMeterInformation? meterInformation = null;

        try
        {
            sessionControl2 = (IAudioSessionControl2)sessionControl;
            audioVolume = (ISimpleAudioVolume)sessionControl;
            meterInformation = (IAudioMeterInformation)sessionControl;

            Marshal.ThrowExceptionForHR(sessionControl.GetState(out var state));
            Marshal.ThrowExceptionForHR(sessionControl.GetDisplayName(out var displayName));
            Marshal.ThrowExceptionForHR(sessionControl.GetIconPath(out var iconPath));
            Marshal.ThrowExceptionForHR(sessionControl2.GetSessionIdentifier(out var sessionIdentifier));
            Marshal.ThrowExceptionForHR(sessionControl2.GetSessionInstanceIdentifier(out var sessionInstanceIdentifier));
            Marshal.ThrowExceptionForHR(sessionControl2.GetProcessId(out var processId));
            var isSystemSoundsSession = sessionControl2.IsSystemSoundsSession() == 0;

            Marshal.ThrowExceptionForHR(audioVolume.GetMute(out var isMuted));
            Marshal.ThrowExceptionForHR(meterInformation.GetPeakValue(out var peakLevel));

            var processName = ResolveProcessName((int)processId, isSystemSoundsSession);
            var normalizedDisplayName = displayName ?? "";
            var normalizedIconPath = iconPath ?? "";
            var normalizedSessionIdentifier = sessionIdentifier ?? "";
            var normalizedInstanceIdentifier = sessionInstanceIdentifier ?? "";

            return new AudioSessionSnapshot(
                SessionId: BuildSessionId(endpointId, (int)processId, processName, normalizedDisplayName, normalizedInstanceIdentifier),
                EndpointId: endpointId,
                ProcessId: (int)processId,
                ProcessName: processName,
                DisplayName: normalizedDisplayName,
                IconPath: normalizedIconPath,
                SessionIdentifier: normalizedSessionIdentifier,
                SessionInstanceIdentifier: normalizedInstanceIdentifier,
                State: state.ToString().ToLowerInvariant(),
                IsSystemSoundsSession: isSystemSoundsSession,
                IsMuted: isMuted,
                PeakLevel: peakLevel,
                CapturedAtUtc: capturedAtUtc);
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(meterInformation);
            ReleaseComObject(audioVolume);
            ReleaseComObject(sessionControl2);
        }
    }

    private static string ResolveProcessName(int processId, bool isSystemSoundsSession)
    {
        if (isSystemSoundsSession)
        {
            return "SystemSounds";
        }

        if (processId <= 0)
        {
            return "";
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string BuildSessionId(string endpointId, int processId, string processName, string displayName, string instanceIdentifier) =>
        $"{endpointId}|{processId}|{processName}|{displayName}|{instanceIdentifier}";

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    private enum EDataFlow
    {
        Render = 0
    }

    private enum ERole
    {
        Multimedia = 1
    }

    [Flags]
    private enum DeviceState : uint
    {
        Active = 0x00000001
    }

    private enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2
    }

    [Flags]
    private enum CLSCTX : uint
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        RemoteServer = 0x10,
        All = InprocServer | InprocHandler | LocalServer | RemoteServer
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(uint storageAccessMode, out IPropertyStore properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out DeviceState state);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A7DAF6F61E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int deviceCount);
        int Item(int deviceIndex, out IMMDevice device);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint propertyCount);
        int GetAt(uint propertyIndex, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId { get; } = formatId;
        public uint PropertyId { get; } = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)] private ushort _variantType;
        [FieldOffset(8)] private IntPtr _pointerValue;

        public string? GetValue() =>
            _variantType == 31 && _pointerValue != IntPtr.Zero
                ? Marshal.PtrToStringUni(_pointerValue)
                : null;

        public void Dispose()
        {
            _ = PropVariantClear(ref this);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IntPtr sessionControl);
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out IntPtr audioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        int RegisterSessionNotification(IntPtr sessionNotification);
        int UnregisterSessionNotification(IntPtr sessionNotification);
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int SessionCount);
        int GetSession(int SessionCount, out IAudioSessionControl Session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string value);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string value);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out AudioSessionState state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string value);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string value);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string value);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string value);
        int GetProcessId(out uint value);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid eventContext);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
    }
}
