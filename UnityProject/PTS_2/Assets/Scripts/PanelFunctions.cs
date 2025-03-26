using System.Collections.Generic;
using UnityEngine;

public class PanelFunctions : MonoBehaviour
{
    [Tooltip("Manually prioritized UI elements that should appear first.")]
    public List<Transform> uiElements;
    public Transform canvasElement;

    // Start is called before the first frame update
    void Awake()
    {
        OrderPanelItems();
    }


    void Start()
    {
       
       
    
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OrderPanelItems(){

         // Get all children that are not in the uiElements list.
        List<Transform> allChildren = new();
        foreach(var child in allChildren){
            if(!uiElements.Contains(child)){
                allChildren.Add(child);
            }
        }

        List<Transform> finalOrder = new();
        finalOrder.AddRange(uiElements);
        finalOrder.AddRange(allChildren);

        for(var i = 0; i < finalOrder.Count; i++){
            finalOrder[i].SetSiblingIndex(i);
        }

    }
}
