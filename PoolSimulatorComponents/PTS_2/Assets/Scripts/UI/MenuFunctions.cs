using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuFunctions : MonoBehaviour
{
    public void PlayGame()
    {
        var sceneToLoad = AppSettings.Instance.Settings.ApiMode switch
        {

            ApiMode.PassThroughCameraAPI => "PassThroughCameraScene",
            ApiMode.SemanticLabeling => "PoolSetup",
            _ => null
        };

        if (!string.IsNullOrEmpty(sceneToLoad) && SceneExists(sceneToLoad))
        {
            PrepareRequiredSettings(sceneToLoad);

            SceneManager.LoadScene(sceneToLoad);
        }
        else
        {
            Debug.LogError($"Scene {sceneToLoad} not found in build settings.");
        }
    }

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

    private bool SceneExists(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            var scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            var scene = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (scene == sceneName)
                return true;
        }

        return false;
    }

    private void PrepareRequiredSettings(string sceneToLoad)
    {

        if (sceneToLoad == "SemanticLabelingScene")
        {
            if (AppSettings.Instance.Settings.ScanControl == ScanControl.ReScanScene)
            {
                // ClearSavedData();
            }
        }
    }
}
