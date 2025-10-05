using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LiveMenuFunctions : MonoBehaviour
{
    [SerializeField] private Button _lockButton;
    [SerializeField] private TextMeshProUGUI label;

    private bool _isLocked = false;

    public void Start()
    {
        if (_lockButton != null)
            _lockButton.onClick.AddListener(ToggleLock);
        _isLocked = PocketMarkerService.Instance != null && PocketMarkerService.Instance.IsLocked;
        UpdateUI();
    }

    private void ToggleLock()
    {
        _isLocked = !_isLocked;
        PocketMarkerService.Instance.SetLocked(_isLocked);
        UpdateUI();
    }
    private void UpdateUI()
    {
        if (label == null) Debug.LogError("There should be some labeling about the button.");
        label.text = _isLocked ? "Unlock pockets" : "Lock pockets";
        label.color = _isLocked? Color.red : Color.green;
    }
}
