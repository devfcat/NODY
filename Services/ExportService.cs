using System.IO;
using System.IO.Compression;
using IOPath = System.IO.Path;

namespace Nody.Services;

public static class ExportService
{
    /// <summary>프로젝트 폴더 전체를 zip 으로 묶는다. (zip 안에 프로젝트 폴더명이 최상위 폴더로 들어간다)</summary>
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

            // 제외: 삭제된 노드 보관 폴더, 엑셀 잠금/임시 파일
            if (rel.StartsWith(ProjectService.TrashFolder + IOPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("~") || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

            var entry = zip.CreateEntry(rootName + "/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
            using var es = entry.Open();
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.CopyTo(es);
        }
    }
}
