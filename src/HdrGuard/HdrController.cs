using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HdrGuard;

internal sealed class HdrController
{
    public IReadOnlyList<DisplayState> GetDisplays()
    {
        var paths = DisplayConfigNative.QueryActivePaths();
        var displays = new List<DisplayState>();

        foreach (var path in paths)
        {
            var key = new DisplayKey(
                path.targetInfo.adapterId.LowPart,
                path.targetInfo.adapterId.HighPart,
                path.targetInfo.id);

            var colorInfo = DisplayConfigNative.GetAdvancedColorInfo(key);
            var colorInfo2 = DisplayConfigNative.TryGetAdvancedColorInfo2(key);
            var name = DisplayConfigNative.TryGetTargetName(key) ?? $"Display {key.TargetId}";
            var hdrSupported = colorInfo2 is null
                ? (colorInfo.value & 0x1) != 0
                : (colorInfo2.Value.value & 0x10) != 0;
            var hdrEnabled = colorInfo2 is null
                ? (colorInfo.value & 0x2) != 0
                : (colorInfo2.Value.value & 0x20) != 0;
            var hdrLimitedByPolicy = colorInfo2 is null
                ? (colorInfo.value & 0x8) != 0
                : (colorInfo2.Value.value & 0x8) != 0;

            displays.Add(new DisplayState(
                key,
                name,
                hdrSupported,
                hdrEnabled,
                hdrLimitedByPolicy,
                colorInfo.bitsPerColorChannel));
        }

        return displays;
    }

    public DisplayState? TryGetDisplay(DisplayKey key) =>
        GetDisplays().FirstOrDefault(display => display.Key.Equals(key));

    public int SetHdrForAllSupportedDisplays(bool enabled)
    {
        var changed = 0;
        foreach (var display in GetDisplays().Where(display => display.AdvancedColorSupported))
        {
            if (display.AdvancedColorEnabled == enabled)
            {
                continue;
            }

            SetHdr(display.Key, enabled);
            changed++;
        }

        return changed;
    }

    public void SetHdr(DisplayKey key, bool enabled)
    {
        DisplayConfigNative.SetAdvancedColorState(key, enabled);
    }
}

internal readonly record struct DisplayKey(uint AdapterLowPart, int AdapterHighPart, uint TargetId);

internal sealed record DisplayState(
    DisplayKey Key,
    string Name,
    bool AdvancedColorSupported,
    bool AdvancedColorEnabled,
    bool AdvancedColorForceDisabled,
    uint BitsPerColorChannel);

internal static class DisplayConfigNative
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const uint QDC_VIRTUAL_MODE_AWARE = 0x00000010;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_SUCCESS = 0;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const int DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;

    public static DISPLAYCONFIG_PATH_INFO[] QueryActivePaths()
    {
        var flags = QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            ThrowIfWin32Error(result, "GetDisplayConfigBufferSizes");

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            result = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (result == ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }

            ThrowIfWin32Error(result, "QueryDisplayConfig");
            return paths.Take((int)pathCount).ToArray();
        }

        throw new Win32Exception(ERROR_INSUFFICIENT_BUFFER, "Display configuration changed while querying active paths.");
    }

    public static DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO GetAdvancedColorInfo(DisplayKey key)
    {
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = CreateHeader(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(), key)
        };

        var result = DisplayConfigGetDeviceInfo(ref info);
        ThrowIfWin32Error(result, "DisplayConfigGetDeviceInfo(GET_ADVANCED_COLOR_INFO)");
        return info;
    }

    public static DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2? TryGetAdvancedColorInfo2(DisplayKey key)
    {
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
        {
            header = CreateHeader(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(), key)
        };

        var result = DisplayConfigGetDeviceInfo(ref info);
        return result == ERROR_SUCCESS ? info : null;
    }

    public static void SetAdvancedColorState(DisplayKey key, bool enabled)
    {
        var state = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            header = CreateHeader(DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE, Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(), key),
            value = enabled ? 1u : 0u
        };

        var result = DisplayConfigSetDeviceInfo(ref state);
        ThrowIfWin32Error(result, "DisplayConfigSetDeviceInfo(SET_ADVANCED_COLOR_STATE)");
    }

    public static string? TryGetTargetName(DisplayKey key)
    {
        var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = CreateHeader(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), key),
            monitorFriendlyDeviceName = new char[64],
            monitorDevicePath = new char[128]
        };

        var result = DisplayConfigGetDeviceInfo(ref name);
        return result == ERROR_SUCCESS
            ? NullIfWhiteSpace(new string(name.monitorFriendlyDeviceName)) ?? NullIfWhiteSpace(new string(name.monitorDevicePath))
            : null;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER CreateHeader(int type, int size, DisplayKey key) => new()
    {
        type = type,
        size = (uint)size,
        adapterId = new LUID
        {
            LowPart = key.AdapterLowPart,
            HighPart = key.AdapterHighPart
        },
        id = key.TargetId
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('\0').Trim();

    private static void ThrowIfWin32Error(int error, string api)
    {
        if (error != ERROR_SUCCESS)
        {
            throw new Win32Exception(error, $"{api} failed with Win32 error {error}.");
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setPacket);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECTL
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public int scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public int pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL pathSourceSize;
        public RECTL desktopImageRegion;
        public RECTL desktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_MODE_INFO
    {
        public int infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public uint bitsPerColorChannel;
        public int activeColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64, ArraySubType = UnmanagedType.U2)]
        public char[] monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128, ArraySubType = UnmanagedType.U2)]
        public char[] monitorDevicePath;
    }
}
