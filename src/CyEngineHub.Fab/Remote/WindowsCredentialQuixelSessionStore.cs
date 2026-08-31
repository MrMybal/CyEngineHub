using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

public sealed class WindowsCredentialQuixelSessionStore : IQuixelSessionStore
{
    private const string ManifestTarget = "CyEngineHub/QuixelSession/v1/manifest";
    private const string ChunkPrefix = "CyEngineHub/QuixelSession/v1/chunk/";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int MaximumCredentialBytes = 5 * 512;
    private const int ChunkBytes = 2_400;
    private const int MaximumChunks = 32;

    public Task<QuixelSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        var manifestBytes = ReadCredential(ManifestTarget);
        if (manifestBytes is null) return Task.FromResult<QuixelSession?>(null);

        byte[]? payload = null;
        try
        {
            var manifest = JsonSerializer.Deserialize<CredentialManifest>(manifestBytes)
                ?? throw new QuixelException("Le manifeste de session Megascans est vide.");
            if (manifest.Version != 1 || manifest.ChunkCount is < 1 or > MaximumChunks)
            {
                throw new QuixelException("Le manifeste de session Megascans est invalide.");
            }

            using var stream = new MemoryStream();
            for (var index = 0; index < manifest.ChunkCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = ReadCredential(GetChunkTarget(index))
                    ?? throw new QuixelException("La session Megascans sécurisée est incomplète.");
                stream.Write(chunk);
                Array.Clear(chunk);
            }

            payload = stream.ToArray();
            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(payload)),
                    manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new QuixelException("L’intégrité de la session Megascans est invalide.");
            }

            return Task.FromResult<QuixelSession?>(
                JsonSerializer.Deserialize<QuixelSession>(payload)
                ?? throw new QuixelException("La session Megascans est vide."));
        }
        catch (JsonException exception)
        {
            throw new QuixelException("La session Megascans sécurisée est illisible.", exception);
        }
        finally
        {
            Array.Clear(manifestBytes);
            if (payload is not null) Array.Clear(payload);
        }
    }

    public Task SaveAsync(QuixelSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        var payload = JsonSerializer.SerializeToUtf8Bytes(session);
        var chunkCount = checked((payload.Length + ChunkBytes - 1) / ChunkBytes);
        if (chunkCount is < 1 or > MaximumChunks)
        {
            Array.Clear(payload);
            throw new QuixelException("La session Megascans dépasse la capacité sécurisée prise en charge.");
        }

        try
        {
            for (var index = 0; index < chunkCount; index++)
            {
                var offset = index * ChunkBytes;
                var length = Math.Min(ChunkBytes, payload.Length - offset);
                var chunk = payload.AsSpan(offset, length).ToArray();
                try
                {
                    WriteCredential(GetChunkTarget(index), chunk, session.DisplayName);
                }
                finally
                {
                    Array.Clear(chunk);
                }
            }

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new CredentialManifest(
                1,
                chunkCount,
                Convert.ToHexString(SHA256.HashData(payload))));
            try
            {
                WriteCredential(ManifestTarget, manifestBytes, session.DisplayName);
            }
            finally
            {
                Array.Clear(manifestBytes);
            }

            for (var index = chunkCount; index < MaximumChunks; index++)
            {
                DeleteCredential(GetChunkTarget(index));
            }
        }
        catch
        {
            ClearCredentials();
            throw;
        }
        finally
        {
            Array.Clear(payload);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        ClearCredentials();
        return Task.CompletedTask;
    }

    private static byte[]? ReadCredential(string target)
    {
        if (!CredRead(target, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new QuixelException(
                "Impossible de lire la session Megascans dans le coffre Windows.",
                new Win32Exception(error));
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void WriteCredential(string target, byte[] bytes, string userName)
    {
        if (bytes.Length is < 1 or > MaximumCredentialBytes)
        {
            throw new QuixelException("Un segment de session Megascans dépasse la limite du coffre Windows.");
        }
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = string.IsNullOrWhiteSpace(userName) ? "Epic/Quixel" : userName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new QuixelException(
                    "Impossible d’enregistrer la session Megascans dans le coffre Windows.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static void ClearCredentials()
    {
        DeleteCredential(ManifestTarget);
        for (var index = 0; index < MaximumChunks; index++) DeleteCredential(GetChunkTarget(index));
    }

    private static void DeleteCredential(string target)
    {
        if (CredDelete(target, CredentialTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != 1168)
        {
            throw new QuixelException(
                "Impossible de supprimer la session Megascans du coffre Windows.",
                new Win32Exception(error));
        }
    }

    private static string GetChunkTarget(int index) => $"{ChunkPrefix}{index:D2}";

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Le stockage sécurisé Megascans utilise le Gestionnaire d’identification Windows.");
        }
    }

    private sealed record CredentialManifest(int Version, int ChunkCount, string Sha256);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
