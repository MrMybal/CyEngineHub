using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CyEngineHub.Epic.Acquisition;

internal static class EpicPatchFileSafety
{
    // In-place writes must not alter another installation through a hard link.
    internal static void RequireSingleLink(FileStream file)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Le patch en place sécurisé est actuellement pris en charge sous Windows.");
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var info) || info.NumberOfLinks != 1)
            throw new IOException("Le fichier moteur possède plusieurs liens physiques ou leur vérification est impossible. Aucun octet ne sera remplacé.");
    }

    internal static void PublishJournal(string temporary, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileExW(temporary, destination, 0x1 | 0x8)) // replace-existing, write-through
                throw new IOException("Impossible de publier durablement le journal du patch.");
        }
        else File.Move(temporary, destination, overwrite: true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string destination, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}
