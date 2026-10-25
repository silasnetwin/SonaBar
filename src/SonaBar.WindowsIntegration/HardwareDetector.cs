using System.Runtime.InteropServices;
using SonaBar.Core;

namespace SonaBar.WindowsIntegration;

/// <summary>
/// Reads the capabilities of the local machine. Every probe is optional: a missing API or a
/// driver that refuses to answer reduces what SonaBar reports, it never prevents startup.
/// </summary>
public static class HardwareDetector
{
    public static HardwareProfile Detect(string? dataDirectory = null)
    {
        var memory = ReadMemory();
        var graphics = ReadGraphicsAdapter();
        return new HardwareProfile(
            OperatingSystem: DescribeOperatingSystem(),
            LogicalProcessors: Math.Max(1, Environment.ProcessorCount),
            TotalMemoryBytes: memory.Total,
            AvailableMemoryBytes: memory.Available,
            GraphicsAdapter: graphics.Name,
            GraphicsMemoryBytes: graphics.Memory,
            DiscreteGraphics: graphics.Discrete,
            SoftwareAdapter: graphics.Software,
            VulkanRuntime: RuntimePresent("vulkan-1.dll"),
            CudaRuntime: RuntimePresent("nvcuda.dll"),
            FreeDiskBytes: ReadFreeDisk(dataDirectory));
    }

    private static string DescribeOperatingSystem()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            return $"{Environment.OSVersion.Platform} {version.Major}.{version.Minor}.{version.Build}";
        }
        catch (InvalidOperationException) { return "Windows"; }
    }

    private static (long Total, long Available) ReadMemory()
    {
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
            return ((long)status.ullTotalPhys, (long)status.ullAvailPhys);
        }
        catch (DllNotFoundException) { return (0, 0); }
        catch (EntryPointNotFoundException) { return (0, 0); }
    }

    private static (string? Name, long Memory, bool Discrete, bool Software) ReadGraphicsAdapter()
    {
        try
        {
            var factoryId = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
            if (CreateDXGIFactory1(ref factoryId, out var factory) != 0 || factory is null) return (null, 0, false, false);
            string? best = null; long bestMemory = 0; var discrete = false; var software = false;
            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1? adapter;
                try { factory.EnumAdapters1(index, out adapter); }
                catch (COMException) { break; }
                if (adapter is null) break;
                try
                {
                    adapter.GetDesc1(out var description);
                    var name = description.Description?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (description.Flags != 0) { software = true; continue; }
                    var memory = (long)Math.Min(description.DedicatedVideoMemory, long.MaxValue);
                    if (memory > bestMemory) { bestMemory = memory; best = name; }
                    // A discrete adapter is reported with a dedicated memory budget that an
                    // integrated adapter never reaches.
                    if (memory >= 1024L * 1024 * 1024) discrete = true;
                }
                finally { Marshal.ReleaseComObject(adapter); }
                if (index > 16) break;
            }
            Marshal.ReleaseComObject(factory);
            return (best, bestMemory, discrete, software && best is null);
        }
        catch (DllNotFoundException) { return (null, 0, false, false); }
        catch (EntryPointNotFoundException) { return (null, 0, false, false); }
        catch (COMException) { return (null, 0, false, false); }
        catch (InvalidCastException) { return (null, 0, false, false); }
        catch (MarshalDirectiveException) { return (null, 0, false, false); }
        catch (ArgumentException) { return (null, 0, false, false); }
    }

    private static bool RuntimePresent(string library)
    {
        try
        {
            if (NativeLibrary.TryLoad(library, out var handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }
        catch (BadImageFormatException) { return false; }
        catch (DllNotFoundException) { return false; }
        return false;
    }

    private static long ReadFreeDisk(string? dataDirectory)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(dataDirectory) ? Brand.DataRoot : dataDirectory;
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return 0;
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
        catch (ArgumentException) { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

    [ComImport, Guid("770AAE78-F26F-4DBA-A829-253C83D1B387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        void SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
        void GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        void GetParent(out object parent);
        void EnumAdapters(uint adapter, out IntPtr result);
        void MakeWindowAssociation(IntPtr windowHandle, uint flags);
        void GetWindowAssociation(out IntPtr windowHandle);
        int CreateSwapChain(IntPtr device, IntPtr description, out IntPtr swapChain);
        int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        void EnumAdapters1(uint adapter, out IDXGIAdapter1 result);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038F61-3839-4626-91FD-086879011A05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        void SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
        void GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        void GetParent(out object parent);
        void EnumOutputs(uint output, out IntPtr result);
        void GetDesc(IntPtr description);
        int CheckInterfaceSupport(ref Guid guidInterface, out long userData);
        void GetDesc1(out DXGI_ADAPTER_DESC1 description);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public ulong AdapterLuid;
        public uint Flags;
    }
}
