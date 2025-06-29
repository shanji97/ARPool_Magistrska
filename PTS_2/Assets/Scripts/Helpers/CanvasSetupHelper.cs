using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

// [RequireComponent(typeof(RectTransform))]
// [RequireComponent(typeof(BoxCollider))]
public class CanvasSetupHelper : MonoBehaviour
{
    public RectTransform canvasRect;
    public BoxCollider canvasCollider;

    private bool isCanvcasSyncedWithCollider = false;


    // Start is called before the first frame update
    void Start()
    {
        if (isCanvcasSyncedWithCollider)
            SyncSizeWithCollider(canvasRect, canvasCollider);
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void SyncSizeWithCollider(RectTransform canvas, BoxCollider collider)
    {
        if (canvas == null || collider == null)
        {
            Debug.LogError("Canvas or Collider is not set");
            return;

        }
            Vector3 size = new(canvas.rect.width, canvas.rect.height, 1);
            isCanvcasSyncedWithCollider = true;
            collider.size = size;
    }
}
