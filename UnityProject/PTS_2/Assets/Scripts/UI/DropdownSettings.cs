using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Dropdown))]
public class SettingsDropdown : MonoBehaviour, ISettingsBindable, ISettingsReactive
{
    public string SettingsKey;
    public string defaultValue;

    public TMP_Dropdown dropdown;
    private List<string> _options = new();
    private string currentValue = string.Empty;

    public List<string> Options
    {
        get
        {
            return _options == null || !_options.Any() ?
                 GenerateOptionsFromSettings(AppSettings.Instance.Settings) :
                 _options;
        }
    }

    // Unity events
    public void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        LoadFromSettings(AppSettings.Instance.Settings);
    }

    public void OnEnable()
    {
        AppSettings.OnSettingsChanged += OnSettingsChanged;
    }
    
    public void OnDisable()
    {
        AppSettings.OnSettingsChanged -= OnSettingsChanged;
    }

    // General 
    public void LoadFromSettings(UserSettings settings)
    {
        SettingsKey = string.IsNullOrEmpty(SettingsKey) ? gameObject.name : SettingsKey;

        _options = GenerateOptionsFromSettings(settings);
        dropdown.ClearOptions();
        dropdown.AddOptions(_options);
        currentValue = GetSettingValue(settings);

        var index = _options.IndexOf(currentValue);
        index = index == -1 ? _options.IndexOf(defaultValue) : index;
        var optionsPresent = _options.Any();
        if (index == -1 && optionsPresent)
            index = 0;
        else if (index == -1 && !optionsPresent)
            Debug.LogError($"No element present in the {_options} collection with the settings key value {SettingsKey}");

        dropdown.SetValueWithoutNotify(index);

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(i =>
        {
            currentValue = _options[i];
            AppSettings.Instance.SetAndSave(SaveToSettings);
        });
    }

    public void SaveToSettings(UserSettings settings)
    {
        SetSettingValue(settings, currentValue);
    }

    private string GetSettingValue(UserSettings settings)
    {
        return SettingsKey switch
        {
            nameof(settings.SelectedLabel) => settings.SelectedLabel,
            nameof(settings.ApiMode) => settings.ApiMode.ToString(),
            nameof(settings.ScanControl) => settings.ScanControl.ToString(),
            _ => defaultValue
        };
    }

    private void SetSettingValue(UserSettings settings, string value)
    {
        switch (SettingsKey)
        {
            case nameof(settings.SelectedLabel):
                settings.SelectedLabel = value;
                break;
            case nameof(settings.ApiMode):
                if (Enum.TryParse(value, out ApiMode mode))
                    settings.ApiMode = mode;
                break;
            case nameof(settings.ScanControl):
                if (Enum.TryParse(value, out ScanControl control))
                    settings.ScanControl = control;
                break;
            default:
                Debug.LogWarning($"Unknown setting key: {SettingsKey}");
                break;
        }
    }

    private List<string> GenerateOptionsFromSettings(UserSettings settings)
    {
        return SettingsKey switch
        {
            nameof(settings.SelectedLabel) => SemanticLabel.GetCandidatesForCueBallDetection(),

            nameof(settings.ApiMode) => Enum.GetNames(typeof(ApiMode)).ToList(),

            nameof(settings.ScanControl) => Enum.GetNames(typeof(ScanControl)).ToList(),

            // Add more if needed

            _ => new List<string>()
        };
    }

    public void OnSettingsChanged(UserSettings settings)
    {
        if (SettingsKey == nameof(settings.SelectedLabel))
        {
            bool shouldEnable = settings.ApiMode == ApiMode.SemanticLabeling;
            dropdown.interactable = shouldEnable;
            _options = GenerateOptionsFromSettings(settings);
            LoadFromSettings(settings);
        }
    }
}