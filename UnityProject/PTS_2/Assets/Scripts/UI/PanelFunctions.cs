using System.Collections.Generic;
using UnityEngine;

public class PanelFunctions : MonoBehaviour
{
    [Tooltip("Manually prioritized UI elements that should appear first.")]
    public List<Transform> UiElements;
    public Transform CanvasElement;

    // Start is called before the first frame update
    void Awake()
    {
#if !DEVELOPMENT_BUILD && !UNITY_EDITOR
        DestroyPlaygroundButton();
#endif
        OrderPanelItems();
    }

#if !DEVELOPMENT_BUILD && !UNITY_EDITOR
    private void DestroyPlaygroundButton()
    {
        Transform toRemove = UiElements.Find(x => x.name == "PLAYGROUND_BUTTON");
        toRemove = toRemove == null ? GameObject.Find("PLAYGROUND_BUTTON")?.transform : toRemove;
        if(toRemove != null)
        {
            UiElements.Remove(toRemove);
            Destroy(toRemove.gameObject);
        }
    }
#endif

    private void OrderPanelItems(){

         // Get all children that are not in the uiElements list.
        List<Transform> allChildren = new();
        foreach(Transform child in CanvasElement){
            if(!UiElements.Contains(child)){
                allChildren.Add(child);
            }
        }

        List<Transform> finalOrder = new();
        finalOrder.AddRange(UiElements);
        finalOrder.AddRange(allChildren);

        for(var i = 0; i < finalOrder.Count; i++){
            finalOrder[i].SetSiblingIndex(i);
        }
    }
}
