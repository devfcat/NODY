using System.IO;
using System.Text.Json;
using System.Windows;
using Nody.Models;

namespace Nody.Services;

public static class VersionService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AppVersion Current { get; } = Load();

    private static AppVersion Load()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/version.json"));
            if (info?.Stream == null) return new AppVersion();

            using var reader = new StreamReader(info.Stream);
            return JsonSerializer.Deserialize<AppVersion>(reader.ReadToEnd(), Options) ?? new AppVersion();
        }
        catch
        {
            return new AppVersion();
        }
    }
}
