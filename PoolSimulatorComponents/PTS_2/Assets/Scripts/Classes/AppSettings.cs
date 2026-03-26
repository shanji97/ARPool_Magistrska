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

    public UserSettings Settings { get; private set; } = new UserSettings();

    public static event Action<UserSettings> OnSettingsChanged;

    public static bool HasInstance => _instance != null;

    private static AppSettings _instance;

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
        Load();
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    public void Save()
    {
        File.WriteAllText(Path, JsonConvert.SerializeObject(Settings, Formatting.Indented));
        OnSettingsChanged?.Invoke(Settings);
    }

    public void SetAndSave(Action<UserSettings> action)
    {
        action(Settings);
        Save();
    }

    public void Load()
    {
        if (!File.Exists(Path))
        {
            Settings = new UserSettings();
            Save();
        }
        else
        {
            var json = File.ReadAllText(Path);
            try
            {
                Settings = JsonConvert.DeserializeObject<UserSettings>(json) ?? new UserSettings();
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to load settings. Default settings are going to be applied. \r\nException message: " + e.Message);
                Settings = new UserSettings();
            }
        }
    }

    public void ResetSettings()
    {
        Settings = new UserSettings();
        Save();
    }

    public void AddTableStateEntries(List<TableStateEntry> tableStateEntries)
    {
        if (!tableStateEntries.Any())
            Debug.Log("No table state entries are being loaded in.");

        //(Settings.TableStates ??= new List<TableStateEntry>()).AddRange(tableStateEntries);
        Save();
    }

    public void SetGameMode(GameMode mode) => GameMode = mode;
}