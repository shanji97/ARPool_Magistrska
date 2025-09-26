using TMPro;
using UnityEngine;

public class PocketLockController : MonoBehaviour
{
    [Tooltip("Optional label showing current lock state")]
    public TextMeshProUGUI StatusLabel;

    public void ToggleLock()
    {
        var svc = PocketMarkerService.Instance;
        if (svc == null) return;

        svc.SetLocked(!svc.IsLocked);
        UpdateLabel();
    }

    public void LockAll()
    {
        if (PocketMarkerService.Instance != null)
        {
            PocketMarkerService.Instance.SetLocked(true);
            UpdateLabel();
        }
    }

    public void UnlockAll()
    {
        if (PocketMarkerService.Instance != null)
        {
            PocketMarkerService.Instance.SetLocked(false);
            UpdateLabel();
        }
    }

    private void OnEnable() => UpdateLabel();
    private void Start() => UpdateLabel();

    private void UpdateLabel()
    {
        if (StatusLabel == null) return;
        var svc = PocketMarkerService.Instance;
        StatusLabel.text = (svc != null && svc.IsLocked) ? "Pockets: LOCKED" : "Pockets: UNLOCKED";
    }
}