using System.Text.Json;

namespace Nody.AndroidApp.Services;

public class AppVersion
{
    public string Name { get; set; } = "NODY";
    public string Version { get; set; } = "1.0.0";
    public string Date { get; set; } = "";
    public string Description { get; set; } = "";
}

public static class VersionService
{
    private static AppVersion _current = new();
    private static bool _loaded;

    public static AppVersion Current => _current;

    public static async Task EnsureLoaded()
    {
        if (_loaded) return;
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("version.json");
            using var reader = new StreamReader(stream);
            _current = JsonSerializer.Deserialize<AppVersion>(await reader.ReadToEndAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppVersion();
        }
        catch
        {
            _current = new AppVersion();
        }
        _loaded = true;
    }
}
