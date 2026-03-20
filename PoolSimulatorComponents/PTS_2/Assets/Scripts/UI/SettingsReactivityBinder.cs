using System.Linq;
using UnityEngine;


public class SettingsReactivityBinder : MonoBehaviour
{
    private void OnEnable()
    {
        AppSettings.OnSettingsChanged += BroadcastSettings;
    }

    private void OnDisable()
    {
        AppSettings.OnSettingsChanged -= BroadcastSettings;
    }

    private void BroadcastSettings(UserSettings settings)
    {
        foreach (var reactive in FindObjectsOfType<MonoBehaviour>().OfType<ISettingsReactive>())
        {
            reactive.OnSettingsChanged(settings);
        }
    }
}