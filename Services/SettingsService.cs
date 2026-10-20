using System.IO;
using System.Text.Json;
using YMusicMini.Models;

namespace YMusicMini.Services;

public sealed class SettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YMusicMini", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path))
                    ?? new AppSettings();
        }
        catch (Exception)
        {
            // Повреждённые настройки не должны мешать запуску.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var text = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, text);
    }
}
