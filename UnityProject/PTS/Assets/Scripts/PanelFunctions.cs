using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class PanelFunctions : MonoBehaviour
{
    public List<Transform> uiElements;
    public Transform canvasElement;

    // Start is called before the first frame update
    void Awake()
    {
#if UNITY_EDITOR
        Debug.Log(uiElements.Count);
        if (!uiElements.Any())
            Debug.LogError("No UI elements found");
        else
        {
            foreach (var element in uiElements)
            {
                Debug.Log($"UI Element: {element.name}");
            }
        }
#endif
    }


    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {

    }
}
