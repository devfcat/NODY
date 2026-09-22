using System.Text.Json;
using System.Text.Json.Serialization;
using Nody.Models;
using IOPath = System.IO.Path;

namespace Nody.AndroidApp.Services;

public static class ProjectService
{
    public const string FlowFileName = "nody_flow.json";
    public const string TrashFolder = "_trash";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultFolder =>
        IOPath.Combine(FileSystem.AppDataDirectory, "projects", "default");

    public static ProjectData Load(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = IOPath.Combine(folder, FlowFileName);
        if (!File.Exists(path)) return new ProjectData();

        try
        {
            return JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path), Options) ?? new ProjectData();
        }
        catch
        {
            try { File.Copy(path, path + ".bak", true); } catch { }
            return new ProjectData();
        }
    }

    public static void Save(string folder, ProjectData data)
    {
        Directory.CreateDirectory(folder);
        var path = IOPath.Combine(folder, FlowFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, path, true);
    }
}

public static class SettingsService
{
    private static string FilePath => IOPath.Combine(FileSystem.AppDataDirectory, "settings.json");

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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
        }
        catch { }
    }
}
