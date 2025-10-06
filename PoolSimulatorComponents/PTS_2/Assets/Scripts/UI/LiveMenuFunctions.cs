using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class LiveMenuFunctions : MonoBehaviour
{
    [SerializeField] private Button _lockButton;
    [SerializeField] private TextMeshProUGUI _lock_button_label;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private TextMeshProUGUI _final_lock_button_label;

    private bool _isLocked = false;

    public void Start()
    {
        if (_lockButton) _lockButton.onClick.AddListener(ToggleLock);
        if (_confirmButton) _confirmButton.onClick.AddListener(ConfirmLock);
        _isLocked = PocketMarkerService.Instance != null && PocketMarkerService.Instance.IsLocked;
        UpdateUI();
    }

    private void ToggleLock()
    {
        _isLocked = !_isLocked;
        PocketMarkerService.Instance.SetLocked(_isLocked);
        UpdateUI();
    }

    private void ConfirmLock()
    {
        var svc = PocketMarkerService.Instance;
        if (svc == null) return;
        svc.FinalizeLocked();
        if (_final_lock_button_label) _final_lock_button_label.text = "Locked (final)"; 
        if (_lockButton) _lockButton.interactable = false;
        if (_confirmButton) _confirmButton.interactable = false;
    }

    private void UpdateUI()
    {
        if (_lock_button_label == null) Debug.LogError("There should be some labeling about the button.");
        _lock_button_label.text = _isLocked ? "Unlock pockets" : "Lock pockets";
        _lock_button_label.color = _isLocked? Color.red : Color.green;
    }
}
