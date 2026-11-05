using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length!=2) throw new ArgumentException("Usage: ManualPayloadSmoke <app.zip> <source-root>");
using var archive=ZipFile.OpenRead(Path.GetFullPath(args[0]));
var root=Path.Combine(Path.GetFullPath(args[1]),"artifacts","cydoc","help");
var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories);
var expected=new HashSet<string>(files.Select(source=>"Help/"+Path.GetRelativePath(root,source).Replace('\\','/')),StringComparer.Ordinal);
if(archive.Entries.Any(entry=>entry.FullName.StartsWith("Help/",StringComparison.Ordinal)&&entry.Name.Length>0&&!expected.Contains(entry.FullName)))throw new InvalidDataException("Unexpected file in the offline documentation payload.");
foreach(var source in files)
{
    var name="Help/"+Path.GetRelativePath(root,source).Replace('\\','/');
    var entry=archive.GetEntry(name)??throw new InvalidDataException("Missing exported documentation resource: "+name);
    using var stream=entry.Open();
    if(!SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new InvalidDataException("Documentation differs from verified export: "+name);
}
using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));
if(manifest.RootElement.GetProperty("defaultLocale").GetString()!="en")throw new InvalidDataException("Wrong default language.");
if(archive.Entries.Any(e=>e.FullName.StartsWith("Help/media-",StringComparison.Ordinal)&&e.FullName.EndsWith(".js",StringComparison.Ordinal)&&e.FullName!="Help/media-image.js"))throw new InvalidDataException("Unexpected media module.");
Console.WriteLine($"PASS: {files.Length} offline CyDoc files match the verified export SHA-256, including all translations, screenshots, scripts and notices.");
