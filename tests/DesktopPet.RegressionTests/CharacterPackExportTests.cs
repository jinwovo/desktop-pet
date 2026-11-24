using System.IO.Compression;
using System.Text.Json;
using DesktopPet.Core;

public static class CharacterPackExportTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Export ZIP round-trips Unicode nested frames and excludes unrelated local files", () => Fixture((root, source, output) =>
        {
            File.WriteAllText(Path.Combine(source, ".env"), "DO_NOT_EXPORT=true");
            File.WriteAllText(Path.Combine(source, "settings.json"), "private local settings");
            var manifest = File.ReadAllBytes(Path.Combine(source, "character.json"));
            var destination = CharacterPackExporter.ExportFolder(source, Path.Combine(output, "shared.zip"));
            var name = Path.GetFileName(source);
            using (var zip = ZipFile.OpenRead(destination))
            {
                Check(zip.Entries.Select(e => e.FullName).ToHashSet().SetEquals(new[] { name+"/character.json", name+"/poses/idle.png", name+"/poses/walk.png" }), "Export included unrelated files or lost declared resources");
                Check(Read(zip, name+"/character.json").SequenceEqual(manifest) && Read(zip, name+"/poses/idle.png").SequenceEqual(new byte[] { 1, 2, 3 }), "Export changed source bytes");
            }
            using var archive = CharacterArchive.Open(destination);
            var library = new CharacterLibrary(Path.Combine(root,"bundled"),Path.Combine(root,"installed"));
            var imported = archive.Import(archive.Characters.Single(), library);
            Check(imported == name && CharacterLoader.Load(library.Resolve(imported)).BehaviorProfile.Description == "Shared profile" &&
                File.ReadAllBytes(Path.Combine(library.Resolve(imported),"character.json")).SequenceEqual(manifest), "Export cannot be imported with its name/profile intact");
        }));
        yield return ("Export refuses existing files/directories without validation or replacement", () => Fixture((root, source, output) =>
        {
            var path = Path.Combine(output,"existing.zip");
            File.WriteAllText(path,"sentinel");
            var validated = false;
            Reject(() => CharacterPackExporter.ExportFolder(source,path,_ => validated=true));
            Check(!validated && File.ReadAllText(path)=="sentinel", "Existing output changed or unnecessary validation ran");
            var directory = Path.Combine(output,"directory.zip");
            Directory.CreateDirectory(directory);
            Reject(() => CharacterPackExporter.ExportFolder(source,directory));
            Check(Directory.Exists(directory),"Existing directory changed");
        }));
        yield return ("Export cancellation before/during validation leaves no public or partial ZIP", () => Fixture((root, source, output) =>
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Cancelled(() => CharacterPackExporter.ExportFolder(source,Path.Combine(output,"before.zip"),cancellationToken:cancelled.Token));
            using var cancellation = new CancellationTokenSource();
            string? scratch = null;
            Cancelled(() => CharacterPackExporter.ExportFolder(source,Path.Combine(output,"during.zip"),stage =>
            {
                scratch=Directory.GetParent(Directory.GetParent(stage)!.FullName)!.FullName;
                cancellation.Cancel();
            },cancellation.Token));
            Check(!Directory.EnumerateFileSystemEntries(output).Any() && scratch is not null && !Directory.Exists(scratch), "Cancellation left staging or output behind");
        }));
        yield return ("Export cancellation while compressing removes partial output and reports bounded progress", () => Fixture((root, source, output) =>
        {
            using var cancellation = new CancellationTokenSource();
            var seen = new List<CharacterExportProgress>();
            Cancelled(() => CharacterPackExporter.ExportFolder(source,Path.Combine(output,"cancel.zip"),cancellationToken:cancellation.Token,
                progress:new InlineProgress<CharacterExportProgress>(p => { seen.Add(p); if(p.CompletedFiles==1) cancellation.Cancel(); })));
            Check(seen.Count==2 && seen[0].CompletedFiles==0 && seen[1].CompletedFiles==1 && seen.All(p=>p.TotalFiles==3) &&
                !Directory.EnumerateFileSystemEntries(output).Any(),"Mid-write cancellation left a partial or continued progress");
        }));
        yield return ("Export validation failure releases all source handles and leaves no ZIP", () => Fixture((root, source, output) =>
        {
            var validated=false;
            Reject(() => CharacterPackExporter.ExportFolder(source,Path.Combine(output,"invalid.zip"),_ => { validated=true; throw new InvalidDataException("Decode rejected"); }));
            Check(validated && !Directory.EnumerateFileSystemEntries(output).Any(),"Validator was skipped or failure published output");
            using var unlocked=File.Open(Path.Combine(source,"poses","idle.png"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }));
        yield return ("Export uses its validated snapshot when the original source later changes", () => Fixture((root, source, output) =>
        {
            var destination=CharacterPackExporter.ExportFolder(source,Path.Combine(output,"snapshot.zip"),_=>
                File.WriteAllBytes(Path.Combine(source,"poses","idle.png"),new byte[]{9,9,9}));
            using var zip=ZipFile.OpenRead(destination);
            Check(Read(zip,Path.GetFileName(source)+"/poses/idle.png").SequenceEqual(new byte[]{1,2,3}),"Export reopened mutable source instead of snapshot");
        }));
        yield return ("Export rejects source-contained, non-ZIP and missing-parent destinations", () => Fixture((root, source, output) =>
        {
            Reject(()=>CharacterPackExporter.ExportFolder(source,Path.Combine(source,"nested.zip")));
            Reject(()=>CharacterPackExporter.ExportFolder(source,Path.Combine(output,"file.txt")));
            Reject(()=>CharacterPackExporter.ExportFolder(source,Path.Combine(output,"missing","pack.zip")));
            Check(!Directory.EnumerateFileSystemEntries(output).Any() && !File.Exists(Path.Combine(source,"nested.zip")),"Rejected path created output");
        }));
        yield return ("Export bytes are reproducible and missing declared resources cannot be exported", () => Fixture((root, source, output) =>
        {
            var first=CharacterPackExporter.ExportFolder(source,Path.Combine(output,"first.zip"));
            var second=CharacterPackExporter.ExportFolder(source,Path.Combine(output,"second.ZIP"));
            Check(File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second)),"Same snapshot produced different ZIP bytes");
            File.Delete(Path.Combine(source,"poses","walk.png"));
            Reject(()=>CharacterPackExporter.ExportFolder(source,Path.Combine(output,"missing.zip")));
            Check(!File.Exists(Path.Combine(output,"missing.zip")),"Missing PNG published a partial character");
        }));
        yield return ("Export never replaces a destination created during compression", () => Fixture((root, source, output) =>
        {
            var destination=Path.Combine(output,"raced.zip");
            Reject(()=>CharacterPackExporter.ExportFolder(source,destination,progress:new InlineProgress<CharacterExportProgress>(p=>
            { if(p.CompletedFiles==p.TotalFiles) File.WriteAllText(destination,"created by another writer"); })));
            Check(File.ReadAllText(destination)=="created by another writer" && Directory.GetFiles(output).Length==1,"Concurrent writer was replaced or partial output leaked");
        }));
        yield return ("Export callback failure cannot publish or leave partial files", () => Fixture((root, source, output) =>
        {
            Reject(()=>CharacterPackExporter.ExportFolder(source,Path.Combine(output,"callback.zip"),progress:new InlineProgress<CharacterExportProgress>(p=>
            { if(p.CompletedFiles==1) throw new IOException("Progress callback failed"); })));
            Check(!Directory.EnumerateFileSystemEntries(output).Any(),"Callback failure left output behind");
        }));
        yield return ("Export refuses linked source and linked output folders", () => Fixture((root, source, output) =>
        {
            var sourceLink=Path.Combine(root,"source-link"); var outputLink=Path.Combine(root,"output-link");
            try { Directory.CreateSymbolicLink(sourceLink,source); Directory.CreateSymbolicLink(outputLink,output); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { throw new RegressionTestSkippedException("Cannot create symbolic link fixture: "+ex.GetType().Name); }
            try
            {
                Reject(()=>CharacterPackExporter.ExportFolder(sourceLink,Path.Combine(output,"link.zip")));
                Reject(()=>CharacterPackExporter.ExportFolder(source,Path.Combine(outputLink,"link.zip")));
                Check(!Directory.EnumerateFileSystemEntries(output).Any(),"Linked export wrote an output");
            }
            finally { Directory.Delete(sourceLink); Directory.Delete(outputLink); }
        }));
    }
    private static byte[] Read(ZipArchive archive,string name)
    {
        using var input=archive.GetEntry(name)!.Open(); using var memory=new MemoryStream(); input.CopyTo(memory); return memory.ToArray();
    }
    private static void Reject(Action run)
    {
        try { run(); } catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException) { return; }
        throw new Exception("Invalid export succeeded");
    }
    private static void Cancelled(Action run)
    { try {run();} catch(OperationCanceledException){return;} throw new Exception("Cancelled export succeeded"); }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private sealed class InlineProgress<T>(Action<T> report):IProgress<T>{public void Report(T value)=>report(value);}
    private static void Fixture(Action<string,string,string> run)
    {
        var temp=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var root=Path.Combine(temp,"DesktopPet-ExportTests-"+Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"공유 친구"); var output=Path.Combine(root,"output");
        Directory.CreateDirectory(Path.Combine(source,"poses")); Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(source,"poses","idle.png"),new byte[]{1,2,3});
        File.WriteAllBytes(Path.Combine(source,"poses","walk.png"),new byte[]{4,5,6});
        File.WriteAllText(Path.Combine(source,"character.json"),JsonSerializer.Serialize(new
        { name="Display Friend",frameSizePx=new{width=128,height=128},anchor=new{x=64,y=122},frames=new{idle=new[]{"poses/idle.png"},walk=new[]{"poses/walk.png"}},behavior=new{description="Shared profile"} }));
        try{run(root,source,output);}
        finally
        {
            Check(Path.GetFullPath(root).StartsWith(temp+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("DesktopPet-ExportTests-",StringComparison.Ordinal),"Unsafe test cleanup");
            Directory.Delete(root,true);
        }
    }
}
