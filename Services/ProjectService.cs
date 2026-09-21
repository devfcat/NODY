using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nody.Models;
using IOPath = System.IO.Path;

namespace Nody.Services;

public static class ProjectService
{
    public const string FlowFileName = "nody_flow.json";
    public const string TrashFolder = "_trash";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProjectData Load(string folder)
    {
        var path = IOPath.Combine(folder, FlowFileName);
        if (!File.Exists(path)) return new ProjectData();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectData>(json, Options) ?? new ProjectData();
        }
        catch
        {
            // 손상된 파일은 백업해 두고 빈 프로젝트로 시작
            try { File.Copy(path, path + ".bak", true); } catch { }
            return new ProjectData();
        }
    }

    /// <summary>임시 파일에 쓴 뒤 교체 → 저장 도중 종료되어도 기존 파일이 깨지지 않는다.</summary>
    public static void Save(string folder, ProjectData data)
    {
        var path = IOPath.Combine(folder, FlowFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, path, true);
    }
}

public static class SettingsService
{
    private static string FilePath => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NODY", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(IOPath.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
        }
        catch { }
    }
}
