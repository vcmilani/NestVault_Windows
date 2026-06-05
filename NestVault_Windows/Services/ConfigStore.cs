using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using NestVault_Windows.Models;

namespace NestVault_Windows.Services;

public partial class ConfigStore : ObservableObject
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NestVault");

    private static readonly string ProfilesPath = Path.Combine(AppDataDir, "profiles.json");
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private static Dictionary<string, string> _settingsCache = [];

    [ObservableProperty] private List<BackupProfile> _profiles = [];

    public ConfigStore() { Load(); }

    // MARK: - Profiles

    public void Load()
    {
        try
        {
            if (!File.Exists(ProfilesPath)) return;
            var json = File.ReadAllText(ProfilesPath);
            Profiles = JsonSerializer.Deserialize<List<BackupProfile>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch { /* ignore corrupt data */ }
    }

    private void Persist()
    {
        EnsureDirectory();
        var json = JsonSerializer.Serialize(Profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProfilesPath, json);
    }

    public void Add(BackupProfile profile)    { Profiles = [.. Profiles, profile]; Persist(); }
    public void Delete(BackupProfile profile) { Profiles = [.. Profiles.Where(p => p.Id != profile.Id)]; Persist(); }

    public void Update(BackupProfile profile)
    {
        var idx = Profiles.FindIndex(p => p.Id == profile.Id);
        if (idx < 0) return;
        var list = new List<BackupProfile>(Profiles);
        list[idx] = profile;
        Profiles = list;
        Persist();
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Profiles.Count || to < 0 || to >= Profiles.Count) return;
        var list = new List<BackupProfile>(Profiles);
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        Profiles = list;
        Persist();
    }

    // MARK: - Settings (key-value)

    public static string LoadSetting(string key, string defaultValue = "")
    {
        EnsureSettings();
        return _settingsCache.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public static void SaveSetting(string key, string value)
    {
        EnsureSettings();
        _settingsCache[key] = value;
        PersistSettings();
    }

    // MARK: - Queue Schedule (stored as JSON)

    public static T? LoadJson<T>(string key) where T : class
    {
        var raw = LoadSetting(key, "");
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<T>(raw); }
        catch { return null; }
    }

    public static void SaveJson<T>(string key, T value) where T : class
    {
        SaveSetting(key, JsonSerializer.Serialize(value));
    }

    // MARK: - Helpers

    private static void EnsureSettings()
    {
        if (_settingsCache.Count > 0) return;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _settingsCache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }
        }
        catch { }
    }

    private static void PersistSettings()
    {
        EnsureDirectory();
        var json = JsonSerializer.Serialize(_settingsCache, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(AppDataDir))
            Directory.CreateDirectory(AppDataDir);
    }
}
