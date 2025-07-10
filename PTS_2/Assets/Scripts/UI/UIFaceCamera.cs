using UnityEngine;

public class UIFaceCamera:MonoBehaviour
{
    private void LateUpdate()
    {
        var camera = Camera.main;
        if (camera == null) return;
        transform.position = camera.transform.position + camera.transform.forward * 1.2f + camera.transform.up * 0.3f;
        transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position);
    }
}
