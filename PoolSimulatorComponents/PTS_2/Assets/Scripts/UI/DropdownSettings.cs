using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Dropdown))]
public class DropdownSettings : MonoBehaviour, ISettingsBindable, ISettingsReactive
{
    public string SettingsKey;
    public string defaultValue;

    public TMP_Dropdown dropdown;
    private List<string> _options = new();
    private string currentValue = string.Empty;

    public List<string> Options =>
        _options?.Any() == true
            ? _options
            : GenerateOptionsFromSettings(AppSettings.Instance.Settings);

    public void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
    }

    public void Start()
    {
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

    public void LoadFromSettings(UserSettings settings)
    {
        SettingsKey = string.IsNullOrEmpty(SettingsKey) ? gameObject.name : SettingsKey;

        settings.EnsureDefaults();

        _options = GenerateOptionsFromSettings(settings);
        dropdown.ClearOptions();
        dropdown.AddOptions(_options);

        currentValue = GetSettingValue(settings);

        int index = _options.IndexOf(currentValue);

        if (index == -1)
        {
            int defaultIndex = _options.IndexOf(defaultValue);
            index = defaultIndex >= 0 ? defaultIndex : (_options.Any() ? 0 : -1);
        }

        if (index == -1)
        {
            Debug.LogError($"No element present in the {_options} collection with the settings key value {SettingsKey}");
            return;
        }

        dropdown.SetValueWithoutNotify(index);

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(i =>
        {
            if (i < 0 || i >= _options.Count)
                return;

            currentValue = _options[i];
            AppSettings.Instance.SetAndSave(SaveToSettings);
        });
    }

    public void SaveToSettings(UserSettings settings)
    {
        settings.EnsureDefaults();
        SetSettingValue(settings, currentValue);
    }

    private string GetSettingValue(UserSettings settings)
    {
        settings.EnsureDefaults();

        return SettingsKey switch
        {
            nameof(settings.SelectedLabel) => settings.SelectedLabel,
            nameof(settings.ApiMode) => settings.ApiMode.ToString(),
            nameof(settings.ScanControl) => settings.ScanControl.ToString(),
            nameof(settings.DeviceInformation) => settings.DeviceInformation.ToString(),
            nameof(GameplayControlSettings.XAxisCorrectionStepMM) => GameplayControlSettings.ToDisplayValue(settings.GameplayControlSettings.XAxisCorrectionStepMM),
            nameof(GameplayControlSettings.ZAxisCorrectionStepMM) => GameplayControlSettings.ToDisplayValue(settings.GameplayControlSettings.ZAxisCorrectionStepMM),
            _ => defaultValue
        };
    }

    private void SetSettingValue(UserSettings settings, string value)
    {
        settings.EnsureDefaults();

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

            case nameof(settings.DeviceInformation):
                if (Enum.TryParse(value, out DeviceInformation deviceInformation))
                    settings.DeviceInformation = deviceInformation;
                break;

            case nameof(GameplayControlSettings.XAxisCorrectionStepMM):
                if (GameplayControlSettings.TryParseDisplayValue(value, out byte xStep))
                    settings.GameplayControlSettings.SetXAxisCorrectionStep(xStep);
                break;

            case nameof(GameplayControlSettings.ZAxisCorrectionStepMM):
                if (GameplayControlSettings.TryParseDisplayValue(value, out byte zStep))
                    settings.GameplayControlSettings.SetZAxisCorrectionStep(zStep);
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
            nameof(settings.DeviceInformation) => Enum.GetNames(typeof(DeviceInformation)).ToList(),
            nameof(GameplayControlSettings.XAxisCorrectionStepMM) => GameplayControlSettings.GetDisplayOptions(),
            nameof(GameplayControlSettings.ZAxisCorrectionStepMM) => GameplayControlSettings.GetDisplayOptions(),
            _ => new List<string>()
        };
    }

    public void OnSettingsChanged(UserSettings settings)
    {
        if (SettingsKey == nameof(settings.SelectedLabel))
            dropdown.interactable = settings.ApiMode == ApiMode.SemanticLabeling;
        else
            dropdown.interactable = true;

        LoadFromSettings(settings);
    }
}