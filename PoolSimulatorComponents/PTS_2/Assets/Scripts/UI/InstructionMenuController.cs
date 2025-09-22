using UnityEngine;
using TMPro;

public class InstructionMenuController : MonoBehaviour
{
    [Tooltip("Default distance in meters in front of the user to spawn the menu.")]
    public float defaultDistance = 1.5f;

    void Start()
    {
        // Position the menu in front of the user at start
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            // Calculate target position defaultDistance in front of camera
            Vector3 spawnPos = mainCamera.transform.position + mainCamera.transform.forward * defaultDistance;
            // Set menu position
            transform.position = spawnPos;

            // Orient the menu to face the user (only yaw, to keep it upright)
            Vector3 forwardFlat = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
            if (forwardFlat.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
            }
        }
    }

    // (Optional extension hook) Example method to update instruction text at runtime
    public void SetInstructionText(string message)
    {
        TextMeshProUGUI tmpText = GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
        {
            tmpText.text = message;
        }
    }
}