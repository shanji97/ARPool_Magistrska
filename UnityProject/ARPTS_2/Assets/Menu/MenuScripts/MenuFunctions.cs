using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;

[RequireComponent(typeof(BoxCollider))]
public class MenuFunctions : MonoBehaviour
{
    private bool playGroundButtonDestroyed = false;

    void Awake()
    {
        AdjustBoxCollider();
    }

    void Start()
    {
        // if (!Debug.isDebugBuild)
        // {
        //     DestroyButton("PLAYGROUND_BUTTON");
        //     playGroundButtonDestroyed = true;
        // }
    }

    // Update is called once per frame
    void LateUpdate()
    {
        // if (!playGroundButtonDestroyed)
        // {
        //     DestroyButton("PLAYGROUND_BUTTON");
        //     playGroundButtonDestroyed = true;
        // }
    }

    public void QuitApplication()
    {
        if (Application.isEditor)
            Debug.Log("Cannot quit inside the Unity Editor.");
        else
            Application.Quit();
    }

    private void AdjustBoxCollider()
    {
        var boxCollider = GetComponent<BoxCollider>();
        if(boxCollider == null)
        {
            Debug.LogError("BoxCollider is null. Please attach a BoxCollider to the GameObject.");
            return;
        }

        var rectTransform = GetComponent<RectTransform>();
        if(rectTransform == null)
        {
            Debug.LogError("RectTransform is null. Please attach a RectTransform to the GameObject.");
            return;
        }
        
        var sizeOfTransoform2D = rectTransform.sizeDelta;
        float scaleX = transform.lossyScale.x;
        float scaleY = transform.lossyScale.y;
        boxCollider.size = new Vector3(sizeOfTransoform2D.x * scaleX, sizeOfTransoform2D.y * scaleY, 1);
        boxCollider.center = new Vector3(0, 0, 0);
    }

    private void DestroyButton(string buttonName)
    {
        // var button = transform.FirstOrDefault(buttonName).gameObject;
        // if (button != null)
        //     Destroy(button);
    }
}
