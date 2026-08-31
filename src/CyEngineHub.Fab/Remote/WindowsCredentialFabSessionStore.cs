using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

public sealed class WindowsCredentialFabSessionStore : IDirectFabSessionStore
{
    private const string LegacyCredentialTarget = "CyEngineHub/FabDirectSession";
    private const string ManifestCredentialTarget = "CyEngineHub/FabDirectSession/v2/manifest";
    private const string ChunkCredentialPrefix = "CyEngineHub/FabDirectSession/v2/chunk/";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int MaximumCredentialBytes = 5 * 512;
    private const int ChunkBytes = 2_400;
    private const int MaximumChunks = 32;

    public Task<DirectFabSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        var manifestBytes = ReadCredential(ManifestCredentialTarget);
        if (manifestBytes is null)
        {
            return Task.FromResult(ReadLegacySession());
        }

        byte[]? payload = null;
        try
        {
            var manifest = JsonSerializer.Deserialize<CredentialManifest>(manifestBytes)
                ?? throw new DirectFabException("Le manifeste de session Fab sécurisée est vide.");
            if (manifest.Version != 2 || manifest.ChunkCount is < 1 or > MaximumChunks)
            {
                throw new DirectFabException("Le manifeste de session Fab sécurisée est invalide.");
            }

            using var stream = new MemoryStream();
            for (var index = 0; index < manifest.ChunkCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = ReadCredential(GetChunkTarget(index))
                    ?? throw new DirectFabException("La session Fab sécurisée est incomplète.");
                stream.Write(chunk);
                Array.Clear(chunk);
            }

            payload = stream.ToArray();
            var checksum = Convert.ToHexString(SHA256.HashData(payload));
            if (!string.Equals(checksum, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectFabException("L’intégrité de la session Fab sécurisée est invalide.");
            }

            var session = JsonSerializer.Deserialize<DirectFabSession>(payload)
                ?? throw new DirectFabException("La session Fab sécurisée est vide.");
            return Task.FromResult<DirectFabSession?>(session);
        }
        catch (JsonException exception)
        {
            throw new DirectFabException("La session Fab sécurisée est illisible.", exception);
        }
        finally
        {
            Array.Clear(manifestBytes);
            if (payload is not null) Array.Clear(payload);
        }
    }

    public Task SaveAsync(
        DirectFabSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        var payload = JsonSerializer.SerializeToUtf8Bytes(session);
        var chunkCount = checked((payload.Length + ChunkBytes - 1) / ChunkBytes);
        if (chunkCount is < 1 or > MaximumChunks)
        {
            Array.Clear(payload);
            throw new DirectFabException("La session Fab dépasse la capacité sécurisée prise en charge.");
        }

        try
        {
            for (var index = 0; index < chunkCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

            var manifest = new CredentialManifest(
                2,
                chunkCount,
                Convert.ToHexString(SHA256.HashData(payload)));
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            try
            {
                // Le manifeste est écrit en dernier : une session partielle n’est jamais considérée valide.
                WriteCredential(ManifestCredentialTarget, manifestBytes, session.DisplayName);
            }
            finally
            {
                Array.Clear(manifestBytes);
            }

            DeleteCredential(LegacyCredentialTarget);
            for (var index = chunkCount; index < MaximumChunks; index++)
            {
                DeleteCredential(GetChunkTarget(index));
            }
        }
        catch
        {
            ClearV2Credentials();
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
        DeleteCredential(LegacyCredentialTarget);
        ClearV2Credentials();
        return Task.CompletedTask;
    }

    private static DirectFabSession? ReadLegacySession()
    {
        var bytes = ReadCredential(LegacyCredentialTarget);
        if (bytes is null) return null;
        try
        {
            return JsonSerializer.Deserialize<DirectFabSession>(bytes);
        }
        catch (JsonException exception)
        {
            throw new DirectFabException("La session Fab sécurisée historique est illisible.", exception);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static byte[]? ReadCredential(string target)
    {
        if (!CredRead(target, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new DirectFabException(
                "Impossible de lire la session Fab dans le Gestionnaire d’identification Windows.",
                new Win32Exception(error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }
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
            throw new DirectFabException("Un segment de session Fab dépasse la limite du coffre Windows.");
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
                UserName = string.IsNullOrWhiteSpace(userName) ? "Epic/Fab" : userName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new DirectFabException(
                    "Impossible d’enregistrer la session Fab dans le Gestionnaire d’identification Windows.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static void ClearV2Credentials()
    {
        DeleteCredential(ManifestCredentialTarget);
        for (var index = 0; index < MaximumChunks; index++)
        {
            DeleteCredential(GetChunkTarget(index));
        }
    }

    private static void DeleteCredential(string target)
    {
        if (CredDelete(target, CredentialTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != 1168)
        {
            throw new DirectFabException(
                "Impossible de supprimer un segment de session Fab du coffre Windows.",
                new Win32Exception(error));
        }
    }

    private static string GetChunkTarget(int index) => $"{ChunkCredentialPrefix}{index:D2}";

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Le stockage sécurisé Fab utilise actuellement le Gestionnaire d’identification Windows.");
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
