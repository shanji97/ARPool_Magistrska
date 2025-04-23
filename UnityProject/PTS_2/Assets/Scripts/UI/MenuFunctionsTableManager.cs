using UnityEngine;

public class MenuFunctionsTableManager : MonoBehaviour
{
    #region  MAIN MENU

    /// <summary>
    /// Igrnores the table and destroys its grandparent gameobject.
    /// </summary>
    public void IgnoreTable()
    {
        // Destroy the grandparent gameobject of the table
        Destroy(transform.parent.gameObject);
    }

    public void SetUpTableScene()
    {
        var tableManager = FindAnyObjectByType<TableManager>().GetComponent<TableManager>();
        tableManager.SetupTableVisuals(transform.parent.transform);
        // if (TableManager.Instance == null)
        // {
        //     Debug.LogError("TableManager instance is null. Cannot set up table scene.");
        //     return;
        // }

        // Trans
    }


    #endregion
}
