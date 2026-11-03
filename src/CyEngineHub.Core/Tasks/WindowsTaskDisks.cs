using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CyEngineHub.Core.Tasks;

[SupportedOSPlatform("windows")]
internal static class WindowsTaskDisks
{
    internal static IReadOnlyList<HubTaskDisk> Resolve(string path)
    {
        var existing = Path.GetFullPath(path);
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (string.IsNullOrEmpty(parent) || parent == existing) return [];
            existing = parent;
        }
        var mount = new char[1024];
        var volume = new char[1024];
        if (!GetVolumePathNameW(existing, mount, mount.Length) ||
            !GetVolumeNameForVolumeMountPointW(new string(mount).TrimEnd('\0'), volume, volume.Length)) return [];
        using var handle = CreateFileW(new string(volume).TrimEnd('\0', '\\'), 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return [];
        var output = new byte[65536];
        // IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS: DWORD count, 8-byte-aligned DISK_EXTENT[24].
        if (!DeviceIoControl(handle, 0x00560000, null, 0, output, output.Length, out var bytes, 0) || bytes < 32) return [];
        var count = BitConverter.ToInt32(output, 0);
        if (count is < 1 or > 1024 || 8 + count * 24 > bytes) return [];
        return Enumerable.Range(0, count).Select(index => BitConverter.ToUInt32(output, 8 + index * 24)).Distinct()
            .Select(number => new HubTaskDisk("physical-disk:" + number, "disque physique #" + number, 2)).ToArray();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string path, [Out] char[] volumePath, int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string path, [Out] char[] volume, int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[]? input, int inputSize,
        [Out] byte[] output, int outputSize, out int returned, nint overlapped);
}
