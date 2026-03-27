// Attach to: BallMenu GameObject in PoolSetup scene
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Branch: ISSUE-84
// Issue: #84 selected-ball world-space menu controller + type dropdown UI + ignore/include button
public class BallOverrideMenuController : MonoBehaviour
{
    [Header("Menu Root")]
    [SerializeField] private GameObject menuCanvasRoot; // BallMenu/MenuCanvas

    [Header("Optional Rect Used For Height Clamping")]
    [SerializeField] private RectTransform menuHeightReferenceRect; // assign BallMenu/MenuCanvas or the main panel rect

    [Header("Follow Target")]
    [SerializeField] private Vector3 worldOffset = new(0f, 0.24f, 0f);
    [SerializeField] private bool followSelectedBall = true;
    [SerializeField] private bool keepCurrentRotation = true;

    [Header("Height Clamp")]
    [SerializeField] private bool clampBottomHeight = true;
    [SerializeField] private float minimumMenuBottomWorldY = 1.15f;
    [SerializeField] private float fallbackHalfHeightWorld = 0.18f;

    [Header("Behavior")]
    [SerializeField] private bool closeMenuWhenEntryButtonsAreHidden = true;

    [Header("Optional Labels")]
    [SerializeField] private TMP_Dropdown ballTypeDropdown;
    [SerializeField] private TMP_Text detectedTypeText;
    [SerializeField] private TMP_Text currentTypeText;
    [SerializeField] private TMP_Text typeSourceText;
    [SerializeField] private TMP_Text typeStatusText;
    [SerializeField] private TMP_Text selectedOverrideFlagsText;
    [SerializeField] private TMP_Text ignoreStateText;
    [SerializeField] private TMP_Text ignoreButtonLabelText;
    [SerializeField] private Button ignoreDetectedBallButton; // assign IgnoreDetectedBallButton here

    private BallOverrideSelectable _selectedSelectable;
    private Quaternion _initialRotation;
    private bool _suppressDropdownCallback;
    private string _lastTypeStatusMessage = string.Empty;

    private static readonly string[] DropdownOptionLabels =
    {
        "Get data from Python App stream",
        "Cue",
        "Eightball",
        "Striped",
        "Solid"
    };

    private void Awake()
    {
        _initialRotation = transform.rotation;
        InitializeBallTypeDropdown();

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);

        RefreshTexts();
    }

    private void OnEnable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;

        BallOverrideSelectable.EntryButtonsVisibilityChanged += HandleEntryButtonsVisibilityChanged;

        if (ballTypeDropdown != null)
        {
            ballTypeDropdown.onValueChanged.RemoveListener(HandleBallTypeDropdownChanged);
            ballTypeDropdown.onValueChanged.AddListener(HandleBallTypeDropdownChanged);
        }

        UpdateDropdownFromSelectedBall();
        RefreshTexts();
    }

    private void OnDisable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;

        BallOverrideSelectable.EntryButtonsVisibilityChanged -= HandleEntryButtonsVisibilityChanged;

        if (ballTypeDropdown != null)
            ballTypeDropdown.onValueChanged.RemoveListener(HandleBallTypeDropdownChanged);
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

    private void InitializeBallTypeDropdown()
    {
        if (ballTypeDropdown == null)
            return;

        ballTypeDropdown.options.Clear();

        for (int i = 0; i < DropdownOptionLabels.Length; i++)
            ballTypeDropdown.options.Add(new TMP_Dropdown.OptionData(DropdownOptionLabels[i]));

        ballTypeDropdown.SetValueWithoutNotify((int)BallTypeOverrideMenuOption.FromPythonStream);
        ballTypeDropdown.RefreshShownValue();
        ballTypeDropdown.interactable = false;
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable selectable)
    {
        bool selectionObjectChanged = _selectedSelectable != selectable;
        _selectedSelectable = selectable;

        if (selectionObjectChanged)
            _lastTypeStatusMessage = string.Empty;

        bool shouldShow =
            _selectedSelectable != null &&
            _selectedSelectable.RuntimeBall != null &&
            BallOverrideSelectable.AreEntryButtonsGloballyVisible;

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

        UpdateDropdownFromSelectedBall();
        RefreshTexts();
    }

    private void HandleEntryButtonsVisibilityChanged(bool areVisible)
    {
        if (areVisible)
            return;

        if (closeMenuWhenEntryButtonsAreHidden)
        {
            ManualBallOverrideService.Instance?.ClearSelection();
            return;
        }

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);
    }

    public void HandleBallTypeDropdownChanged(int selectedIndex)
    {
        if (_suppressDropdownCallback)
            return;

        Ball selectedBall = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        if (selectedBall == null)
        {
            _lastTypeStatusMessage = "No ball is currently selected.";
            UpdateDropdownFromSelectedBall();
            RefreshTexts();
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= DropdownOptionLabels.Length)
        {
            _lastTypeStatusMessage = "Invalid ball type option.";
            UpdateDropdownFromSelectedBall();
            RefreshTexts();
            return;
        }

        BallTypeOverrideMenuOption option = (BallTypeOverrideMenuOption)selectedIndex;
        string statusMessage = string.Empty;
        bool applied =
            ManualBallOverrideService.Instance != null &&
            ManualBallOverrideService.Instance.TryApplySelectedTypeOverrideOption(option, out statusMessage);

        _lastTypeStatusMessage = statusMessage;
        UpdateDropdownFromSelectedBall();
        RefreshSelectedVisuals();

        if (!applied)
            RefreshTexts();
    }

    public void ToggleIgnoreSelectedBall()
    {
        Ball selectedBall = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        if (selectedBall == null)
            return;

        ManualBallOverrideService.Instance?.ToggleSelectedIgnoredState();
        RefreshSelectedVisuals();
    }

    private void UpdateDropdownFromSelectedBall()
    {
        if (ballTypeDropdown == null)
            return;

        Ball selectedBall = ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

        BallTypeOverrideMenuOption option = BallTypeOverrideMenuOption.FromPythonStream;

        if (selectedBall != null && selectedBall.IsTypeUserOverriden())
            option = MapBallTypeToMenuOption(selectedBall.BallType);

        _suppressDropdownCallback = true;
        ballTypeDropdown.interactable = selectedBall != null;
        ballTypeDropdown.SetValueWithoutNotify((int)option);
        ballTypeDropdown.RefreshShownValue();
        _suppressDropdownCallback = false;
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

        if (ballTypeDropdown != null)
            ballTypeDropdown.interactable = selectedBall != null;

        if (ignoreDetectedBallButton != null)
            ignoreDetectedBallButton.interactable = selectedBall != null;

        if (selectedBall == null)
        {
            if (detectedTypeText != null)
                detectedTypeText.text = "Detected type: -";

            if (currentTypeText != null)
                currentTypeText.text = "Effective type: -";

            if (typeSourceText != null)
                typeSourceText.text = "Type source: -";

            if (typeStatusText != null)
                typeStatusText.text = "Type status: -";

            if (selectedOverrideFlagsText != null)
                selectedOverrideFlagsText.text = "Overrides: -";

            if (ignoreStateText != null)
                ignoreStateText.text = "Ignored state: -";

            if (ignoreButtonLabelText != null)
                ignoreButtonLabelText.text = "Ignore detected ball";

            return;
        }

        if (detectedTypeText != null)
        {
            string detectedTypeLabel = selectedBall.HasDetectedBaseline()
                ? BuildBallTypeDisplayLabel(selectedBall.GetDetectedOrCurrentBallType())
                : "-";

            detectedTypeText.text = $"Detected type: {detectedTypeLabel}";
        }

        if (currentTypeText != null)
            currentTypeText.text = $"Effective type: {BuildBallTypeDisplayLabel(selectedBall.BallType)}";

        if (typeSourceText != null)
            typeSourceText.text = $"Type source: {BuildTypeSourceLabel(selectedBall)}";

        if (typeStatusText != null)
            typeStatusText.text = $"Type status: {BuildTypeStatusLabel(selectedBall)}";

        if (selectedOverrideFlagsText != null)
            selectedOverrideFlagsText.text = $"Overrides: {selectedBall.UserOverrides}";

        if (ignoreStateText != null)
            ignoreStateText.text = $"Ignored state: {BuildIgnoreStateLabel(selectedBall)}";

        if (ignoreButtonLabelText != null)
            ignoreButtonLabelText.text = selectedBall.IsIgnoredByUser()
                ? "Include detected ball"
                : "Ignore detected ball";
    }

    private void RefreshSelectedVisuals()
    {
        _selectedSelectable?.RefreshVisualState();
        RefreshTexts();
    }

    public void SelectCue() => ApplyTypeSelection(BallTypeOverrideMenuOption.Cue);

    public void SelectEight() => ApplyTypeSelection(BallTypeOverrideMenuOption.Eightball);

    public void SelectSolid() => ApplyTypeSelection(BallTypeOverrideMenuOption.Solid);

    public void SelectStripe() => ApplyTypeSelection(BallTypeOverrideMenuOption.Striped);

    private void ApplyTypeSelection(BallTypeOverrideMenuOption option)
    {
        if (ballTypeDropdown != null)
        {
            _suppressDropdownCallback = true;
            ballTypeDropdown.SetValueWithoutNotify((int)option);
            ballTypeDropdown.RefreshShownValue();
            _suppressDropdownCallback = false;
        }

        HandleBallTypeDropdownChanged((int)option);
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
        _lastTypeStatusMessage = "Type now follows Python App stream.";
        UpdateDropdownFromSelectedBall();
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
        _lastTypeStatusMessage = "All selected-ball overrides were reset.";
        UpdateDropdownFromSelectedBall();
        RefreshSelectedVisuals();
    }

    public void CloseMenu()
    {
        ManualBallOverrideService.Instance?.ClearSelection();
    }

    private static BallTypeOverrideMenuOption MapBallTypeToMenuOption(BallType ballType) =>
        ballType switch
        {
            BallType.Cue => BallTypeOverrideMenuOption.Cue,
            BallType.Eight => BallTypeOverrideMenuOption.Eightball,
            BallType.Stripe => BallTypeOverrideMenuOption.Striped,
            BallType.Solid => BallTypeOverrideMenuOption.Solid,
            _ => BallTypeOverrideMenuOption.FromPythonStream
        };

    private static string BuildBallTypeDisplayLabel(BallType ballType) =>
        ballType switch
        {
            BallType.Cue => "Cue",
            BallType.Eight => "Eightball",
            BallType.Stripe => "Striped",
            BallType.Solid => "Solid",
            _ => ballType.ToString()
        };

    private static string BuildIgnoreStateLabel(Ball selectedBall) =>
        selectedBall == null
            ? "-"
            : selectedBall.IsIgnoredByUser()
                ? "Ignored by user override"
                : "Included";

    private string BuildTypeSourceLabel(Ball selectedBall)
    {
        if (selectedBall == null)
            return "-";

        if (selectedBall.IsTypeUserOverriden())
            return "User override";

        return selectedBall.HasDetectedBaseline()
            ? "Python App stream"
            : "No Python App data yet";
    }

    private string BuildTypeStatusLabel(Ball selectedBall)
    {
        if (!string.IsNullOrWhiteSpace(_lastTypeStatusMessage))
            return _lastTypeStatusMessage;

        if (selectedBall == null)
            return "-";

        if (selectedBall.IsTypeUserOverriden())
            return "User type override is active.";

        return selectedBall.HasDetectedBaseline()
            ? "Type follows Python App stream."
            : "Waiting for Python App stream data.";
    }

    private static int GetNextBallId(BallType ballType, int currentBallId, int direction) =>
        ballType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            BallType.Solid => Wrap(currentBallId, 1, 7, direction),
            BallType.Stripe => Wrap(currentBallId, 9, 15, direction),
            _ => currentBallId
        };

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