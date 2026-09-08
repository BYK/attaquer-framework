using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AttaquerTaskbar.Diagnostics;
using AttaquerTaskbar.Models;

namespace AttaquerTaskbar.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AttaquerTaskbar");
        _settingsPath = Path.Combine(directory, "settings.json");
        Current = Load(_settingsPath);
    }

    public TaskbarSettings Current { get; }

    public event Action<TaskbarSettings>? Changed;

    public void Update(Action<TaskbarSettings> update)
    {
        update(Current);
        Save();
        Changed?.Invoke(Current);
    }

    private static TaskbarSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new TaskbarSettings();
            return JsonSerializer.Deserialize<TaskbarSettings>(File.ReadAllText(path), JsonOptions)
                ?? new TaskbarSettings();
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Taskbar settings could not be loaded", exception);
            return new TaskbarSettings();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Taskbar settings could not be saved", exception);
        }
    }
}
