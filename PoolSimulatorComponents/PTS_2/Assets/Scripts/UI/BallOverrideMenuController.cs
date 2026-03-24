// Attach to: BallMenu in PoolSetup scene
using TMPro;
using UnityEngine;

public class BallOverrideMenuController : MonoBehaviour
{
    [Header("Menu Root")]
    [SerializeField] private GameObject menuCanvasRoot; // e.g. BallMenu/MenuCanvas

    [Header("Follow Target")]
    [SerializeField] private Vector3 worldOffset = new(0f, 0.08f, 0f);
    [SerializeField] private bool followSelectedBall = true;
    [SerializeField] private bool keepCurrentRotation = true; // UPDATED: preserves your current 180-degree canvas workaround

    [Header("Optional Labels")]
    [SerializeField] private TMP_Text selectedTypeText;
    [SerializeField] private TMP_Text selectedNumberText;
    [SerializeField] private TMP_Text selectedOverrideFlagsText;

    private BallOverrideSelectable _selectedSelectable;

    private void Awake()
    {
        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;
    }

    private void OnDisable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;
    }

    private void LateUpdate()
    {
        if (_selectedSelectable == null || !followSelectedBall)
            return;

        Transform anchor = _selectedSelectable.MenuAnchor;
        if (anchor == null)
            return;

        transform.position = anchor.position + worldOffset;
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable selectable)
    {
        _selectedSelectable = selectable;

        bool shouldShow = _selectedSelectable != null && _selectedSelectable.RuntimeBall != null;

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(shouldShow);

        RefreshTexts();
    }

    private void RefreshTexts()
    {
        Ball selectedBall = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        if (selectedBall == null)
        {
            if (selectedTypeText != null)
                selectedTypeText.text = "Type: -";

            if (selectedNumberText != null)
                selectedNumberText.text = "Number: -";

            if (selectedOverrideFlagsText != null)
                selectedOverrideFlagsText.text = "Overrides: -";

            return;
        }

        if (selectedTypeText != null)
            selectedTypeText.text = $"Type: {selectedBall.BallType}";

        if (selectedNumberText != null)
            selectedNumberText.text = $"Number: {selectedBall.BallId}";

        if (selectedOverrideFlagsText != null)
            selectedOverrideFlagsText.text = $"Overrides: {selectedBall.UserOverrides}";
    }

    private void RefreshSelectedVisuals()
    {
        _selectedSelectable?.RefreshVisualState();
        RefreshTexts();
    }

    public void SelectCue()
    {
        ManualBallOverrideService.Instance?.ApplySelectedTypeOverride(BallType.Cue);
        RefreshSelectedVisuals();
    }

    public void SelectEight()
    {
        ManualBallOverrideService.Instance?.ApplySelectedTypeOverride(BallType.Eight);
        RefreshSelectedVisuals();
    }

    public void SelectSolid()
    {
        ManualBallOverrideService.Instance?.ApplySelectedTypeOverride(BallType.Solid);
        RefreshSelectedVisuals();
    }

    public void SelectStripe()
    {
        ManualBallOverrideService.Instance?.ApplySelectedTypeOverride(BallType.Stripe);
        RefreshSelectedVisuals();
    }

    public void IncrementBallNumber()
    {
        Ball selectedBall = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        if (selectedBall == null)
            return;

        int next = GetNextBallId(selectedBall.BallType, selectedBall.BallId, +1);
        ManualBallOverrideService.Instance.ApplySelectedBallIdOverride(next);
        RefreshSelectedVisuals();
    }

    public void DecrementBallNumber()
    {
        Ball selectedBall = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        if (selectedBall == null)
            return;

        int next = GetNextBallId(selectedBall.BallType, selectedBall.BallId, -1);
        ManualBallOverrideService.Instance.ApplySelectedBallIdOverride(next);
        RefreshSelectedVisuals();
    }

    public void ReleaseTypeOverride()
    {
        ManualBallOverrideService.Instance?.ReleaseSelectedTypeOverride();
        RefreshSelectedVisuals();
    }

    public void ReleaseBallNumberOverride()
    {
        ManualBallOverrideService.Instance?.ReleaseSelectedBallIdOverride();
        RefreshSelectedVisuals();
    }

    public void ResetSelectedBallOverrides()
    {
        ManualBallOverrideService.Instance?.ResetSelectedOverrides();
        RefreshSelectedVisuals();
    }

    public void CloseMenu()
    {
        ManualBallOverrideService.Instance?.ClearSelection();
    }

    private static int GetNextBallId(BallType ballType, int currentBallId, int direction)
    {
        return ballType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            BallType.Solid => Wrap(currentBallId, 1, 7, direction),
            BallType.Stripe => Wrap(currentBallId, 9, 15, direction),
            _ => currentBallId
        };
    }

    private static int Wrap(int value, int min, int max, int direction)
    {
        int next = value + direction;

        if (next > max)
            return min;

        if (next < min)
            return max;

        return next;
    }
}