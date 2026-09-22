using System.IO.Compression;
using IOPath = System.IO.Path;

namespace Nody.AndroidApp.Services;

public static class ExportService
{
    public static void ExportZip(string projectFolder, string zipPath)
    {
        var root = IOPath.GetFullPath(projectFolder).TrimEnd('\\', '/');
        var rootName = IOPath.GetFileName(root);
        var zipFull = IOPath.GetFullPath(zipPath);

        using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var full = IOPath.GetFullPath(file);
            if (string.Equals(full, zipFull, StringComparison.OrdinalIgnoreCase)) continue;

            var rel = IOPath.GetRelativePath(root, full);
            var name = IOPath.GetFileName(full);
            if (rel.StartsWith(ProjectService.TrashFolder + IOPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("~") || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

            var entry = zip.CreateEntry(rootName + "/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
            using var es = entry.Open();
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.CopyTo(es);
        }
    }
}
