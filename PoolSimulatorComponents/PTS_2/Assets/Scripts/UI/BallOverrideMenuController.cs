using TMPro;
using UnityEngine;

// Attach to: BallMenu GameObject in PoolSetup scene
// Branch: ISSUE-84
// Issue: #84 selected-ball world-space menu controller
public class BallOverrideMenuController : MonoBehaviour
{
    [Header("Menu Root")]
    [SerializeField] private GameObject menuCanvasRoot; // BallMenu/MenuCanvas

    [Header("Optional Rect Used For Height Clamping")]
    [SerializeField] private RectTransform menuHeightReferenceRect; // assign BallMenu/MenuCanvas or the main panel rect

    [Header("Follow Target")]
    [SerializeField] private Vector3 worldOffset = new(0f, 0.24f, 0f); // e.g. menu follow offset above selected ball
    [SerializeField] private bool followSelectedBall = true;
    [SerializeField] private bool keepCurrentRotation = true; // keep your current 180° canvas workaround

    [Header("Height Clamp")]
    [SerializeField] private bool clampBottomHeight = true;
    [SerializeField] private float minimumMenuBottomWorldY = 1.15f;
    [SerializeField] private float fallbackHalfHeightWorld = 0.18f;

    [Header("Behavior")]
    [SerializeField] private bool closeMenuWhenEntryButtonsAreHidden = true; // UPDATED: hiding global "E" buttons also closes the menu.

    [Header("Optional Labels")]
    [SerializeField] private TMP_Text selectedTypeText;
    [SerializeField] private TMP_Text selectedNumberText;
    [SerializeField] private TMP_Text selectedOverrideFlagsText;

    private BallOverrideSelectable _selectedSelectable;
    private Quaternion _initialRotation;

    private void Awake()
    {
        _initialRotation = transform.rotation;

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;

        BallOverrideSelectable.EntryButtonsVisibilityChanged += HandleEntryButtonsVisibilityChanged; // UPDATED
    }

    private void OnDisable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;

        BallOverrideSelectable.EntryButtonsVisibilityChanged -= HandleEntryButtonsVisibilityChanged; // UPDATED
    }

    private void LateUpdate()
    {
        if (_selectedSelectable == null || !followSelectedBall)
            return;

        Transform anchor = _selectedSelectable.MenuAnchor;
        if (anchor == null)
            return;

        Vector3 desiredPosition = anchor.position + worldOffset;

        if (clampBottomHeight)
        {
            float halfHeightWorld = GetHalfHeightWorld();
            float currentBottomY = desiredPosition.y - halfHeightWorld;

            if (currentBottomY < minimumMenuBottomWorldY)
                desiredPosition.y += minimumMenuBottomWorldY - currentBottomY;
        }

        transform.position = desiredPosition;

        if (keepCurrentRotation)
            transform.rotation = _initialRotation;

        RefreshTexts();
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable selectable)
    {
        _selectedSelectable = selectable;

        bool shouldShow =
            _selectedSelectable != null &&
            _selectedSelectable.RuntimeBall != null &&
            BallOverrideSelectable.AreEntryButtonsGloballyVisible; // UPDATED: menu is only reachable while global entry buttons are enabled.

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(shouldShow);

        if (shouldShow)
        {
            Transform anchor = _selectedSelectable.MenuAnchor;
            if (anchor != null)
            {
                Vector3 desiredPosition = anchor.position + worldOffset;

                if (clampBottomHeight)
                {
                    float halfHeightWorld = GetHalfHeightWorld();
                    float currentBottomY = desiredPosition.y - halfHeightWorld;

                    if (currentBottomY < minimumMenuBottomWorldY)
                        desiredPosition.y += minimumMenuBottomWorldY - currentBottomY;
                }

                transform.position = desiredPosition;
            }

            if (keepCurrentRotation)
                transform.rotation = _initialRotation;
        }

        RefreshTexts();
    }

    private void HandleEntryButtonsVisibilityChanged(bool areVisible)
    {
        if (areVisible)
            return;

        if (closeMenuWhenEntryButtonsAreHidden)
        {
            ManualBallOverrideService.Instance?.ClearSelection(); // UPDATED: hiding all "E" buttons also closes the selected-ball menu.
            return;
        }

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);
    }

    private float GetHalfHeightWorld()
    {
        if (menuHeightReferenceRect == null)
            return fallbackHalfHeightWorld;

        return menuHeightReferenceRect.rect.height * menuHeightReferenceRect.lossyScale.y * 0.5f;
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