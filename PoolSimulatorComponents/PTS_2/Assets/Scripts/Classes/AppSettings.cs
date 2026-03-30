using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class AppSettings : MonoBehaviour
{
    private static string Path => System.IO.Path.Combine(Application.persistentDataPath, "userSettings.json");

    public GameMode GameMode { get; private set; } = GameMode.InMenu;

    public UserSettings Settings { get; private set; } = new();

    public static event Action<UserSettings> OnSettingsChanged;

    public static bool HasInstance => _instance != null;

    private static AppSettings _instance;
    private bool _hasLoadedSettings;

    public static AppSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<AppSettings>();

                if (_instance == null)
                {
                    var gameObject = new GameObject("AppSettings");
                    _instance = gameObject.AddComponent<AppSettings>();
                    DontDestroyOnLoad(gameObject);
                }
            }

            _instance.EnsureLoaded();
            return _instance;
        }
    }

    public void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureLoaded();
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    private void EnsureLoaded()
    {
        if (_hasLoadedSettings)
            return;

        Load();
        _hasLoadedSettings = true;
    }

    public void Save()
    {
        Settings ??= new UserSettings();
        Settings.EnsureDefaults();

        File.WriteAllText(Path, JsonConvert.SerializeObject(Settings, Formatting.Indented));
        OnSettingsChanged?.Invoke(Settings);
    }

    public void SetAndSave(Action<UserSettings> action)
    {
        EnsureLoaded();

        action(Settings);
        Settings.EnsureDefaults();
        Save();
    }

    public void Load()
    {
        if (!File.Exists(Path))
        {
            Settings = new UserSettings();
            Settings.EnsureDefaults();
            Save();
            return;
        }

        var json = File.ReadAllText(Path);

        try
        {
            Settings = JsonConvert.DeserializeObject<UserSettings>(json) ?? new UserSettings();
            Settings.EnsureDefaults();
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load settings. Default settings are going to be applied. \r\nException message: " + e.Message);
            Settings = new UserSettings();
            Settings.EnsureDefaults();
        }
    }

    public void ResetSettings()
    {
        Settings = new UserSettings();
        Settings.EnsureDefaults();
        Save();
    }

    public void AddTableStateEntries(List<TableStateEntry> tableStateEntries)
    {
        if (!tableStateEntries.Any())
            Debug.Log("No table state entries are being loaded in.");

        Save();
    }

    public void SetGameMode(GameMode mode) => GameMode = mode;
}