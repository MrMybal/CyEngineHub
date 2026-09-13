[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$icon = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../assets/branding/cyenginehub.ico'))
if (-not ('CyHubIconCheck' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
public static class CyHubIconCheck {
    private delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr arg);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr arg);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    public static int Verify(string executable, string icon) {
        byte[] ico = File.ReadAllBytes(icon);
        if (BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1)
            throw new InvalidDataException("Invalid icon header.");
        int count = BitConverter.ToUInt16(ico, 4);
        var expected = new HashSet<string>();
        for (int i=0; i<count; i++) {
            int length = checked((int)BitConverter.ToUInt32(ico, 6+i*16+8));
            int offset = checked((int)BitConverter.ToUInt32(ico, 6+i*16+12));
            var frame = new byte[length]; Array.Copy(ico, offset, frame, 0, length);
            expected.Add(Convert.ToHexString(SHA256.HashData(frame)));
        }
        if (count != 7 || expected.Count != 7) throw new InvalidDataException("Seven icon frames required.");
        // Data-file loading only: never execute the app or its entry point.
        IntPtr module = LoadLibraryEx(Path.GetFullPath(executable), IntPtr.Zero, 2);
        if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot inspect executable resources.");
        try {
            var actual = new HashSet<string>();
            EnumName callback = (m,t,n,a) => {
                IntPtr resource = FindResource(m,n,t);
                var bytes = new byte[checked((int)SizeofResource(m,resource))];
                Marshal.Copy(LockResource(LoadResource(m,resource)), bytes,0,bytes.Length);
                actual.Add(Convert.ToHexString(SHA256.HashData(bytes))); return true;
            };
            if (!EnumResourceNames(module, new IntPtr(3), callback, IntPtr.Zero) || !expected.IsSubsetOf(actual))
                throw new InvalidDataException("Executable does not embed all seven CyEngineHub icon frames.");
            return count;
        } finally { FreeLibrary(module); }
    }
}
'@
}
$count = [CyHubIconCheck]::Verify([IO.Path]::GetFullPath($Executable), $icon)
Write-Output "$([IO.Path]::GetFileName($Executable)): $count embedded icon frames match the branding ICO."
