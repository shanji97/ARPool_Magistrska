using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach to: BallMenu GameObject in PoolSetup scene
public class BallOverrideMenuController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private BallPositionCorrectionController ballPositionCorrectionController;

    [Header("Menu Root")]
    [SerializeField] private GameObject menuCanvasRoot;

    [Header("Panels")]
    [SerializeField] private GameObject mainOverridePanel;
    [SerializeField] private GameObject positionOverrideMenuPanel;

    [Header("Optional Rect Used For Height Clamping")]
    [SerializeField] private RectTransform menuHeightReferenceRect;

    [Header("Follow Target")]
    [SerializeField] private Vector3 worldOffset = new(0f, 0.24f, 0f);
    [SerializeField] private bool followSelectedBall = true;
    [SerializeField] private bool keepCurrentRotation = true;

    [Header("Frozen Position Correction Menu")]
    [SerializeField] private bool freezeMenuWhilePositionCorrectionIsActive = true;
    [SerializeField] private Vector3 correctionMenuExtraOffset = Vector3.zero;

    [Header("Height Clamp")]
    [SerializeField] private bool clampBottomHeight = true;
    [SerializeField] private float minimumMenuBottomWorldY = 1.15f;
    [SerializeField] private float fallbackHalfHeightWorld = 0.18f;

    [Header("Behavior")]
    [SerializeField] private bool closeMenuWhenSelectionIsCleared = true;
    [SerializeField] private bool verboseLogs = false;

    [Header("Type UI")]
    [SerializeField] private TMP_Dropdown ballTypeDropdown;
    [SerializeField] private TMP_Text detectedTypeText;
    [SerializeField] private TMP_Text currentTypeText;
    [SerializeField] private TMP_Text typeSourceText;
    [SerializeField] private TMP_Text typeStatusText;
    [SerializeField] private TMP_Text selectedOverrideFlagsText;
    [SerializeField] private TMP_Text ignoreStateText;
    [SerializeField] private TMP_Text ignoreButtonLabelText;
    [SerializeField] private Button ignoreDetectedBallButton;

    [Header("Position UI")]
    [SerializeField] private TMP_Text overridePositionText;
    [SerializeField] private Button adjustPositionButton;
    [SerializeField] private Button increaseXPositionButton;
    [SerializeField] private Button decreaseXPositionButton;
    [SerializeField] private Button increaseZPositionButton;
    [SerializeField] private Button decreaseZPositionButton;
    [SerializeField] private Button showOriginalDetectedPositionIfOverrideIsUsedButton;
    [SerializeField] private TMP_Text showOriginalDetectedPositionIfOverrideIsUsedButtonText;
    [SerializeField] private Button resetPositionToPythonDetectionButton;
    [SerializeField] private Button adjustBasicOverridesButton;
    [SerializeField] private Button resetAllOverridesButton;

    private BallOverrideSelectable _selectedSelectable;
    private Quaternion _initialRotation;
    private bool _suppressDropdownCallback;
    private string _lastTypeStatusMessage = string.Empty;

    private bool _hasFrozenMenuPose;
    private Vector3 _frozenMenuWorldPosition;
    private Quaternion _frozenMenuWorldRotation;

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
        ResolveDependencies();

        _initialRotation = transform.rotation;
        InitializeBallTypeDropdown();

        if (menuHeightReferenceRect == null && menuCanvasRoot != null)
            menuHeightReferenceRect = menuCanvasRoot.GetComponent<RectTransform>();

        SetPanelState(showPositionPanel: false);

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);

        RefreshTexts();
    }

    private void OnEnable()
    {
        ResolveDependencies();

        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;

        BallOverrideSelectable.EntryButtonsVisibilityChanged += HandleEntryButtonsVisibilityChanged;

        if (ballPositionCorrectionController != null)
            ballPositionCorrectionController.StateChanged += HandlePositionControllerStateChanged;

        if (ballTypeDropdown != null)
        {
            ballTypeDropdown.onValueChanged.RemoveListener(HandleBallTypeDropdownChanged);
            ballTypeDropdown.onValueChanged.AddListener(HandleBallTypeDropdownChanged);
        }

        SyncSelectionFromService(forceRefresh: true);
        RefreshTexts();
    }

    private void OnDisable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;

        BallOverrideSelectable.EntryButtonsVisibilityChanged -= HandleEntryButtonsVisibilityChanged;

        if (ballPositionCorrectionController != null)
            ballPositionCorrectionController.StateChanged -= HandlePositionControllerStateChanged;

        if (ballTypeDropdown != null)
            ballTypeDropdown.onValueChanged.RemoveListener(HandleBallTypeDropdownChanged);
    }

    private void LateUpdate()
    {
        SyncSelectionFromService(forceRefresh: false);

        bool shouldShow = ShouldShowMenuForCurrentSelection();

        if (menuCanvasRoot != null && menuCanvasRoot.activeSelf != shouldShow)
            menuCanvasRoot.SetActive(shouldShow);

        if (!shouldShow)
        {
            RefreshTexts();
            return;
        }

        if (ShouldUseFrozenMenuPose())
            ApplyFrozenMenuPose();
        else if (followSelectedBall)
            SnapMenuToSelectedBall();

        RefreshTexts();
    }

    private void ResolveDependencies()
    {
        if (ballPositionCorrectionController == null)
            ballPositionCorrectionController = FindFirstObjectByType<BallPositionCorrectionController>();
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

    private void HandleSelectedBallChanged(BallOverrideSelectable _)
    {
        SyncSelectionFromService(forceRefresh: true);
    }

    private void HandleEntryButtonsVisibilityChanged(bool _)
    {
        RefreshTexts();
    }

    private void HandlePositionControllerStateChanged()
    {
        if (!ShouldUseFrozenMenuPose())
            ClearFrozenMenuPose();

        RefreshTexts();
    }

    private void SyncSelectionFromService(bool forceRefresh)
    {
        BallOverrideSelectable serviceSelection =
            ManualBallOverrideService.Instance != null
                ? ManualBallOverrideService.Instance.SelectedSelectable
                : null;

        if (!forceRefresh && ReferenceEquals(_selectedSelectable, serviceSelection))
            return;

        bool changed = !ReferenceEquals(_selectedSelectable, serviceSelection);
        _selectedSelectable = serviceSelection;

        if (changed)
        {
            _lastTypeStatusMessage = string.Empty;
            ClearFrozenMenuPose();

            if (verboseLogs)
                Debug.Log($"[BallOverrideMenuController] Selection changed. HasSelection={_selectedSelectable != null}");
        }

        if (_selectedSelectable == null)
        {
            ballPositionCorrectionController?.HideOriginalDetectedPositionPreview();
            SetPanelState(showPositionPanel: false);

            if (menuCanvasRoot != null)
                menuCanvasRoot.SetActive(false);

            RefreshTexts();
            return;
        }

        UpdateDropdownFromSelectedBall();

        if (menuCanvasRoot != null && !menuCanvasRoot.activeSelf)
            menuCanvasRoot.SetActive(true);

        SnapMenuToSelectedBall();
        RefreshTexts();
    }

    private bool ShouldShowMenuForCurrentSelection() =>
        _selectedSelectable != null &&
        _selectedSelectable.RuntimeBall != null;

    private bool ShouldUseFrozenMenuPose() =>
        freezeMenuWhilePositionCorrectionIsActive &&
        ballPositionCorrectionController != null &&
        ballPositionCorrectionController.IsPositionCorrectionActive &&
        _hasFrozenMenuPose;

    private void CacheFrozenMenuPose()
    {
        _frozenMenuWorldPosition = transform.position + correctionMenuExtraOffset;
        _frozenMenuWorldRotation = keepCurrentRotation ? _initialRotation : transform.rotation;
        _hasFrozenMenuPose = true;
    }

    private void ApplyFrozenMenuPose()
    {
        transform.position = _frozenMenuWorldPosition;
        transform.rotation = _frozenMenuWorldRotation;
    }

    private void ClearFrozenMenuPose()
    {
        _hasFrozenMenuPose = false;
    }

    private void SnapMenuToSelectedBall()
    {
        if (_selectedSelectable == null)
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
    }

    public void OpenPositionOverridePanel()
    {
        Ball selectedBall = GetSelectedBall();
        if (selectedBall == null)
            return;

        if (followSelectedBall)
            SnapMenuToSelectedBall();

        CacheFrozenMenuPose();
        ballPositionCorrectionController?.BeginSelectedBallPositionCorrection();
        SetPanelState(showPositionPanel: true);
        RefreshTexts();
    }

    public void ReturnToMainOverridePanel()
    {
        ballPositionCorrectionController?.ConfirmSelectedBallPositionCorrection();
        ClearFrozenMenuPose();
        SetPanelState(showPositionPanel: false);
        RefreshTexts();
    }

    public void IncreaseXPosition()
    {
        ballPositionCorrectionController?.NudgeSelectedBallPositiveX();
        RefreshTexts();
    }

    public void DecreaseXPosition()
    {
        ballPositionCorrectionController?.NudgeSelectedBallNegativeX();
        RefreshTexts();
    }

    public void IncreaseZPosition()
    {
        ballPositionCorrectionController?.NudgeSelectedBallPositiveZ();
        RefreshTexts();
    }

    public void DecreaseZPosition()
    {
        ballPositionCorrectionController?.NudgeSelectedBallNegativeZ();
        RefreshTexts();
    }

    public void ToggleShowOriginalDetectedPositionPreview()
    {
        ballPositionCorrectionController?.ToggleOriginalDetectedPositionPreview();
        RefreshTexts();
    }

    public void RevertSelectedBallToDetectedPosition()
    {
        ballPositionCorrectionController?.RevertSelectedBallToDetectedPosition();
        ClearFrozenMenuPose();
        SetPanelState(showPositionPanel: false);
        RefreshTexts();
    }

    public void ResetSelectedBallOverrides()
    {
        ballPositionCorrectionController?.HideOriginalDetectedPositionPreview();
        ManualBallOverrideService.Instance?.ResetSelectedOverrides();

        _lastTypeStatusMessage = "All selected-ball overrides were reset.";

        ClearFrozenMenuPose();
        SetPanelState(showPositionPanel: false);
        UpdateDropdownFromSelectedBall();
        RefreshSelectedVisuals();
    }

    public void ResetAllRuntimeBallOverridesForSession()
    {
        ballPositionCorrectionController?.HideOriginalDetectedPositionPreview();

        if (ballPositionCorrectionController != null && ballPositionCorrectionController.IsPositionCorrectionActive)
            ballPositionCorrectionController.ConfirmSelectedBallPositionCorrection();

        TableService.Instance?.ResetRuntimeBallOverridesAndRefreshViews();
        ManualBallOverrideService.Instance?.ClearSelection();

        _lastTypeStatusMessage = "All runtime ball overrides were reset for this session.";

        ClearFrozenMenuPose();
        SetPanelState(showPositionPanel: false);

        if (menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);

        RefreshTexts();
    }

    public void HandleBallTypeDropdownChanged(int selectedIndex)
    {
        if (_suppressDropdownCallback)
            return;

        Ball selectedBall = GetSelectedBall();
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

        string statusMessage = "Manual ball override service is not available.";
        bool applied = false;

        if (ManualBallOverrideService.Instance != null)
            applied = ManualBallOverrideService.Instance.TryApplySelectedTypeOverrideOption(option, out statusMessage);

        _lastTypeStatusMessage = statusMessage;

        UpdateDropdownFromSelectedBall();
        RefreshSelectedVisuals();

        if (!applied)
            RefreshTexts();
    }

    public void ToggleIgnoreSelectedBall()
    {
        Ball selectedBall = GetSelectedBall();
        if (selectedBall == null)
            return;

        ManualBallOverrideService.Instance?.ToggleSelectedIgnoredState();
        RefreshSelectedVisuals();
    }

    public void ReleaseTypeOverride()
    {
        ManualBallOverrideService.Instance?.ReleaseSelectedTypeOverride();
        _lastTypeStatusMessage = "Type now follows Python App stream.";
        UpdateDropdownFromSelectedBall();
        RefreshSelectedVisuals();
    }

    public void CloseMenu()
    {
        ballPositionCorrectionController?.HideOriginalDetectedPositionPreview();

        if (ballPositionCorrectionController != null && ballPositionCorrectionController.IsPositionCorrectionActive)
            ballPositionCorrectionController.ConfirmSelectedBallPositionCorrection();

        ManualBallOverrideService.Instance?.ClearSelection();

        ClearFrozenMenuPose();

        if (closeMenuWhenSelectionIsCleared && menuCanvasRoot != null)
            menuCanvasRoot.SetActive(false);
    }

    private Ball GetSelectedBall() =>
        ManualBallOverrideService.Instance != null
            ? ManualBallOverrideService.Instance.SelectedBall
            : null;

    private void SetPanelState(bool showPositionPanel)
    {
        if (mainOverridePanel != null)
            mainOverridePanel.SetActive(!showPositionPanel);

        if (positionOverrideMenuPanel != null)
            positionOverrideMenuPanel.SetActive(showPositionPanel);
    }

    private void UpdateDropdownFromSelectedBall()
    {
        if (ballTypeDropdown == null)
            return;

        Ball selectedBall = GetSelectedBall();
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
        Ball selectedBall = GetSelectedBall();

        bool hasSelectedBall = selectedBall != null;
        bool correctionActive =
            ballPositionCorrectionController != null &&
            ballPositionCorrectionController.IsPositionCorrectionActive;

        if (ballTypeDropdown != null)
            ballTypeDropdown.interactable = hasSelectedBall;

        if (ignoreDetectedBallButton != null)
            ignoreDetectedBallButton.interactable = hasSelectedBall;

        if (adjustPositionButton != null)
            adjustPositionButton.interactable = hasSelectedBall;

        if (increaseXPositionButton != null)
            increaseXPositionButton.interactable = correctionActive;

        if (decreaseXPositionButton != null)
            decreaseXPositionButton.interactable = correctionActive;

        if (increaseZPositionButton != null)
            increaseZPositionButton.interactable = correctionActive;

        if (decreaseZPositionButton != null)
            decreaseZPositionButton.interactable = correctionActive;

        if (showOriginalDetectedPositionIfOverrideIsUsedButton != null)
            showOriginalDetectedPositionIfOverrideIsUsedButton.interactable =
                ballPositionCorrectionController != null &&
                ballPositionCorrectionController.CanToggleOriginalDetectedPositionPreview;

        if (resetPositionToPythonDetectionButton != null)
            resetPositionToPythonDetectionButton.interactable =
                ballPositionCorrectionController != null &&
                ballPositionCorrectionController.CanRevertSelectedBallToDetectedPosition;

        if (showOriginalDetectedPositionIfOverrideIsUsedButtonText != null)
        {
            bool showingPreview =
                ballPositionCorrectionController != null &&
                ballPositionCorrectionController.IsShowingOriginalDetectedPositionPreview;

            showOriginalDetectedPositionIfOverrideIsUsedButtonText.text =
                showingPreview
                    ? "Hide original detected position"
                    : "Show original detected position";
        }

        if (overridePositionText != null)
        {
            string xStep = ballPositionCorrectionController != null
                ? ballPositionCorrectionController.GetXAxisCorrectionStepDisplay()
                : "-";

            string zStep = ballPositionCorrectionController != null
                ? ballPositionCorrectionController.GetZAxisCorrectionStepDisplay()
                : "-";

            overridePositionText.text = $"Override position | X step {xStep} | Z step {zStep}";
        }

        if (!hasSelectedBall)
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
}