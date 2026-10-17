using CyEngineHub.Fab.Remote;

// Explicit local files only. No network, chunks, session files or manifest output.
if (args.Length == 0) throw new ArgumentException("Pass local .manifest files to validate read-only.");
foreach (var path in args)
{
    if (!Path.GetExtension(path).Equals(".manifest", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Only .manifest inputs are supported.");
    var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
    var manifest = EpicManifestParser.Parse(bytes, new Uri("https://qa.invalid/CloudDir/"));
    Console.WriteLine($"PASS binary manifest: {bytes.Length:N0} bytes, {manifest.Files.Count:N0} files, {manifest.Chunks.Count:N0} chunks, format {manifest.Version}.");
}
