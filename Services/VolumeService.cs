using System.Diagnostics;
using NAudio.CoreAudioApi;
using YMusicMini.Models;

namespace YMusicMini.Services;

public readonly record struct VolumeState(bool Available, double Percent);

/// <summary>
/// Управляет только явно распознанной аудиосессией приложения, либо общей громкостью по выбору.
/// Никакого изменения случайной аудиосессии в случае ошибки определения.
/// </summary>
public sealed class VolumeService
{
    public string[] FindProcesses()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                var name = GetProcessName(session);
                if (name is not null) names.Add(name);
            }
            return names.OrderBy(s => s).ToArray();
        }
        catch { return []; }
    }

    public VolumeState Read(AppSettings settings)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (settings.VolumeTarget == "System")
                return new VolumeState(true, device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);

            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (Matches(session, settings))
                    return new VolumeState(true, session.SimpleAudioVolume.Volume * 100);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Core Audio: {ex.Message}"); }
        return new VolumeState(false, 0);
    }

    public bool SetVolume(AppSettings settings, double percent)
    {
        float value = (float)(Math.Clamp(percent, 0, 100) / 100d);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (settings.VolumeTarget == "System")
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
                return true;
            }

            var sessions = device.AudioSessionManager.Sessions;
            bool changed = false;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (!Matches(session, settings)) continue;
                session.SimpleAudioVolume.Volume = value;
                changed = true;
            }
            return changed;
        }
        catch (Exception ex) { Debug.WriteLine($"Set audio volume: {ex.Message}"); return false; }
    }

    private static bool Matches(AudioSessionControl session, AppSettings settings)
    {
        var name = GetProcessName(session);
        if (name is null) return false;
        if (!string.IsNullOrWhiteSpace(settings.AudioProcessName))
            return name.Equals(settings.AudioProcessName, StringComparison.OrdinalIgnoreCase);

        // Не выбираем браузер или любой процесс с «music» в имени.
        // У разных поколений десктопной Яндекс Музыки имена отличаются.
        // Убираем пробелы, точки и дефисы: «Яндекс Музыка», «Y.Music»,
        // «YandexMusic» и похожие имена сопоставляются явно.
        string normalized = new string(name.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());
        if (normalized is "ymusic" or "yandexmusic" or "яндексмузыка" ||
            normalized.StartsWith("yandexmusic", StringComparison.Ordinal) ||
            normalized.StartsWith("яндексмузыка", StringComparison.Ordinal))
            return true;
        try
        {
            using var process = Process.GetProcessById(checked((int)session.GetProcessID));
            var title = process.MainWindowTitle;
            return title.Contains("Яндекс Музыка", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Yandex Music", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string? GetProcessName(AudioSessionControl session)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)session.GetProcessID));
            return process.ProcessName;
        }
        catch { return null; }
    }
}
