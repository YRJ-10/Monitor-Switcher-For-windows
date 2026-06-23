using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MonitorSwitcher;

public class DisplayManager
{
    private const uint QDC_ALL_PATHS = 1;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint QDC_DATABASE_CURRENT = 4;

    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint SDC_SAVE_TO_DATABASE = 0x00000200;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
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
    public struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public RECTL DesktopImageRegion;
        public RECTL DesktopImageClip;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct RECTL
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(0)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
    {
        DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
        DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2,
        DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [DllImport("User32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("User32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("User32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
        uint numModeInfoArrayElements,
        [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        uint flags);

    public static void SaveProfile(string filename)
    {
        int err = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out uint pathCount, out uint modeCount);
        if (err != 0) throw new Exception($"GetDisplayConfigBufferSizes failed with code {err}");

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        err = QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != 0) throw new Exception($"QueryDisplayConfig failed with code {err}");

        // Save sizes and arrays
        using var fs = new FileStream(filename, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        
        bw.Write(pathCount);
        bw.Write(modeCount);

        WriteStructArray(bw, paths, pathCount);
        WriteStructArray(bw, modes, modeCount);
        
        Console.WriteLine($"Profile saved successfully to {filename}. Paths: {pathCount}, Modes: {modeCount}");
    }

    public static void LoadProfile(string filename)
    {
        if (!File.Exists(filename))
        {
            Console.WriteLine($"Profile file not found: {filename}");
            return;
        }

        using var fs = new FileStream(filename, FileMode.Open);
        using var br = new BinaryReader(fs);

        uint pathCount = br.ReadUInt32();
        uint modeCount = br.ReadUInt32();

        var paths = ReadStructArray<DISPLAYCONFIG_PATH_INFO>(br, pathCount);
        var modes = ReadStructArray<DISPLAYCONFIG_MODE_INFO>(br, modeCount);

        uint flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE;
        int err = SetDisplayConfig(pathCount, paths, modeCount, modes, flags);
        if (err != 0)
        {
            Console.WriteLine($"SetDisplayConfig failed with code {err}. Make sure the monitors connected match the profile.");
        }
        else
        {
            Console.WriteLine($"Profile applied successfully from {filename}");
        }
    }

    private static void WriteStructArray<T>(BinaryWriter bw, T[] array, uint count) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size * count];
        IntPtr ptr = Marshal.AllocHGlobal(size * (int)count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                Marshal.StructureToPtr(array[i], ptr + i * size, false);
            }
            Marshal.Copy(ptr, buffer, 0, buffer.Length);
            bw.Write(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static T[] ReadStructArray<T>(BinaryReader br, uint count) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = br.ReadBytes(size * (int)count);
        T[] array = new T[count];
        IntPtr ptr = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.Copy(buffer, 0, ptr, buffer.Length);
            for (int i = 0; i < count; i++)
            {
                array[i] = Marshal.PtrToStructure<T>(ptr + i * size);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return array;
    }
}
