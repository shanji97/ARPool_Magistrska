using System.Linq;
using UnityEngine;

public class MenuFunctions : MonoBehaviour
{
    public void QuitApplication()
    {
        if (!Application.isEditor)
            Application.Quit();
        else
            Debug.Log("Quit Application from inside the editor");
    }

    public void ResetSettings()
    {
        AppSettings.Instance.ResetSettings();

        foreach (var bindable in FindObjectsOfType<MonoBehaviour>().OfType<ISettingsBindable>())
        {
            bindable.LoadFromSettings(AppSettings.Instance.Settings);
        }
    }
}
