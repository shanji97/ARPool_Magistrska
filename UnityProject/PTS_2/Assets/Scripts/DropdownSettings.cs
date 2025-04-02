using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Dropdown))]
public class SettingsDropdown : MonoBehaviour, ISettingsBindable, ISettingsReactive
{
    public string settingKey;
    public string defaultValue;

    public TMP_Dropdown dropdown;
    private List<string> options = new();
    private string currentValue = string.Empty;

    public List<string> Options
    {
        get
        {
            return options == null || !options.Any() ?
                 GenerateOptionsFromSettings(AppSettings.Instance.Settings) :
                 options;
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
        dropdown.ClearOptions();
        options = GenerateOptionsFromSettings(settings);


        dropdown.AddOptions(options);
        currentValue = GetSettingValue(settings);
        var index = options.IndexOf(currentValue);

        index = index == -1 ? options.IndexOf(defaultValue) : index;
        var optionsPresent = options.Any();
        if (index == -1 && optionsPresent)
            index = 0;
        else if (index == -1 && !optionsPresent)
            Debug.LogError($"No element present in the {options} collection with the settings key value {settingKey}");

        dropdown.SetValueWithoutNotify(index);

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(i =>
        {
            currentValue = options[i];
            AppSettings.Instance.SetAndSave(SaveToSettings);
        });
    }

    public void SaveToSettings(UserSettings settings)
    {
        SetSettingValue(settings, currentValue);
    }

    private string GetSettingValue(UserSettings settings)
    {
        return settingKey switch
        {
            nameof(settings.SelectedLabel) => settings.SelectedLabel,
            nameof(settings.ApiMode) => settings.ApiMode.ToString(),
            _ => defaultValue
        };
    }

    private void SetSettingValue(UserSettings settings, string value)
    {
        switch (settingKey)
        {
            case nameof(settings.SelectedLabel):
                settings.SelectedLabel = value;
                break;
            case nameof(settings.ApiMode):
                if (Enum.TryParse(value, out ApiMode mode))
                    settings.ApiMode = mode;
                break;
            default:
                Debug.LogWarning($"Unknown setting key: {settingKey}");
                break;
        }
    }

    private List<string> GenerateOptionsFromSettings(UserSettings settings)
    {
        return settingKey switch
        {
            nameof(settings.SelectedLabel) => SemanticLabel.GetCandidatesForCueBallDetection(),

            nameof(settings.ApiMode) => Enum.GetNames(typeof(ApiMode)).ToList(),

            // Add more if needed

            _ => new List<string>()
        };
    }

    private void UpdateDropdownOptions()
    {
        if (dropdown == null)
            dropdown = GetComponent<TMP_Dropdown>();

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
    }

    public void OnSettingsChanged(UserSettings settings)
    {
        if (settingKey == nameof(settings.SelectedLabel))
        {
            bool shouldEnable = settings.ApiMode == ApiMode.SemanticLabeling;
            dropdown.interactable = shouldEnable;
            options = GenerateOptionsFromSettings(settings);
            LoadFromSettings(settings);
        }
    }
}