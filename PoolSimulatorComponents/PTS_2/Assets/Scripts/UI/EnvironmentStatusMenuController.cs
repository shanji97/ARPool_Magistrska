using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class EnvironmentStatusMenuController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI environmentParseStatusText;
    [SerializeField] private TextMeshProUGUI pocketParsingStatusText;
    [SerializeField] private TextMeshProUGUI staticMarkingsStatusText;
    [SerializeField] private TextMeshProUGUI qrCodeScanStatusText;

    [SerializeField] private Button confirmPocketPlacementButton;
    [SerializeField] private Button confirmStaticMarkingsPlacementButton;
    [SerializeField] private Button lockQrPositionButton;
    [SerializeField] private Button toggleBallEntryButtonsButton;
    [SerializeField] private Button advanceManualAlignmentButton;

    [Header("Scene References")]
    [SerializeField] private TableStaticMarkingsView tableStaticMarkingsView;
    [SerializeField] private TableQrAlignmentController tableQrAlignmentController;
    [SerializeField] private TableManualAlignmentController tableManualAlignmentController;

    [Header("Status Messages")]
    [TextArea]
    [SerializeField] private string environmentWaitingMessage = "Waiting for environment data to be parsed...";
    [TextArea]
    [SerializeField] private string environmentBackupLoadedMessage = "Backup environment loaded, waiting for new data.";
    [TextArea]
    [SerializeField] private string environmentLiveParsedMessage = "Environment received from data transmission.";
    [TextArea]
    [SerializeField] private string environmentParsedMessage = "Environment successfully parsed.";
    [TextArea]
    [SerializeField] private string pocketsWaitingMessage = "Waiting for pocket data to be received from the detection pipeline...";
    [TextArea]
    [SerializeField] private string pocketsConfirmedMessage = "Pocket placement confirmed.";
    [TextArea]
    [SerializeField] private string staticMarkingsWaitingMessage = "Confirm pocket placement to continue to the static markings phase.";
    [TextArea]
    [SerializeField] private string staticMarkingsAdjustMessage = "Adjust the 1/4 line and rack guide, then confirm static markings.";
    [TextArea]
    [SerializeField] private string staticMarkingsConfirmedMessage = "Static markings confirmed.";
    [TextArea]
    [SerializeField] private string qrWaitingMessage = "No QR code detected. Show a solvable QR set.";

    [Header("Colors")]
    [SerializeField] private Color waitingColor = Color.white;
    [SerializeField] private Color partialColor = new(1f, 0.8f, 0f, 1f);
    [SerializeField] private Color readyColor = Color.green;
    [SerializeField] private Color errorColor = Color.red;
    [SerializeField] private Color enabledButtonLabelColor = Color.green;
    [SerializeField] private Color disabledButtonLabelColor = Color.white;

    [Header("Behavior")]
    [SerializeField] private bool disableConfirmButtonAfterClick = true;
    [SerializeField] private bool invokeParsedEventOnlyOnce = true;
    [SerializeField] private bool requireQrReadyBeforePocketConfirmation = true;
    [SerializeField] private bool hideQrLockButtonAfterPocketConfirmation = true;
    [SerializeField] private bool showBallEntryToggleAfterPocketConfirmation = true;
    [SerializeField] private bool forceHideBallEntryButtonsWhenSetupIsInvalid = true;
    [SerializeField] private bool isQrTemporarilyDisabled = true;
    [SerializeField] private bool showManualAlignmentButtonWhenQrIsDisabled = true;

    [Header("Events")]
    public UnityEvent OnEnvironmentParsed;
    public UnityEvent OnConfirmPocketPlacementRequested;
    public UnityEvent OnConfirmStaticMarkingsPlacementRequested;
    public UnityEvent OnQrLockedAndApplied;
    public UnityEvent OnBallEntryButtonsShown;
    public UnityEvent OnBallEntryButtonsHidden;
    public UnityEvent OnManualAlignmentAdvanced;

    private bool _hasInvokedEnvironmentParsedEvent;
    private string _lastQrStatusMessage = string.Empty;

    private void OnValidate() => AutoResolveSceneReferences();

    private void Awake()
    {
        AutoResolveSceneReferences();
        InitializeUiDefaults();

        if (confirmPocketPlacementButton != null)
        {
            confirmPocketPlacementButton.onClick.RemoveListener(HandleConfirmPocketPlacementClicked);
            confirmPocketPlacementButton.onClick.AddListener(HandleConfirmPocketPlacementClicked);
        }

        if (confirmStaticMarkingsPlacementButton != null)
        {
            confirmStaticMarkingsPlacementButton.onClick.RemoveListener(HandleConfirmStaticMarkingsPlacementClicked);
            confirmStaticMarkingsPlacementButton.onClick.AddListener(HandleConfirmStaticMarkingsPlacementClicked);
        }

        if (lockQrPositionButton != null)
        {
            lockQrPositionButton.onClick.RemoveListener(HandleLockQrPositionClicked);
            lockQrPositionButton.onClick.AddListener(HandleLockQrPositionClicked);
        }

        if (toggleBallEntryButtonsButton != null)
        {
            toggleBallEntryButtonsButton.onClick.RemoveListener(HandleToggleBallEntryButtonsClicked);
            toggleBallEntryButtonsButton.onClick.AddListener(HandleToggleBallEntryButtonsClicked);
        }

        if (advanceManualAlignmentButton != null)
        {
            advanceManualAlignmentButton.onClick.RemoveListener(HandleAdvanceManualAlignmentClicked);
            advanceManualAlignmentButton.onClick.AddListener(HandleAdvanceManualAlignmentClicked);
        }
    }

    private void OnEnable()
    {
        AutoResolveSceneReferences();
        RefreshUiFromTableService(forceRefresh: true);
    }

    private void Update() => RefreshUiFromTableService(forceRefresh: false);

    private void AutoResolveSceneReferences()
    {
        if (tableStaticMarkingsView == null)
            tableStaticMarkingsView = FindFirstObjectByType<TableStaticMarkingsView>();

        if (tableQrAlignmentController == null)
            tableQrAlignmentController = FindFirstObjectByType<TableQrAlignmentController>();

        if (tableManualAlignmentController == null)
            tableManualAlignmentController = FindFirstObjectByType<TableManualAlignmentController>();
    }

    private void InitializeUiDefaults()
    {
        SetStatusText(environmentParseStatusText, environmentWaitingMessage, waitingColor);
        SetStatusText(pocketParsingStatusText, pocketsWaitingMessage, waitingColor);
        SetStatusText(staticMarkingsStatusText, staticMarkingsWaitingMessage, waitingColor);
        SetStatusText(qrCodeScanStatusText, qrWaitingMessage, errorColor);

        SetButtonState(confirmPocketPlacementButton, false, "Confirm pockets");
        SetButtonState(confirmStaticMarkingsPlacementButton, false, "Confirm markings");
        SetButtonState(lockQrPositionButton, false, "Show QR codes");
        SetButtonState(toggleBallEntryButtonsButton, false, "Show ball edit buttons");
        SetButtonState(advanceManualAlignmentButton, false, "Begin placing");

        SetButtonVisible(lockQrPositionButton, true);
        SetButtonVisible(toggleBallEntryButtonsButton, false);
        SetButtonVisible(advanceManualAlignmentButton, false);

        BallOverrideSelectable.SetGlobalEntryButtonsVisible(false);
    }

    private void RefreshUiFromTableService(bool forceRefresh)
    {
        TableService tableService = TableService.Instance;
        TableStaticMarkingsView markingsView = ResolveMarkingsView();
        TableQrAlignmentController qrController = ResolveQrAlignmentController();
        TableManualAlignmentController manualAlignmentController = ResolveManualAlignmentController();

        if (tableService == null)
        {
            if (forceRefresh)
                InitializeUiDefaults();

            return;
        }

        bool environmentParsed = tableService.ArePropertiesParsed();
        bool pocketsParsed = tableService.HasAllPockets();
        bool pocketsConfirmed = tableService.LockFinalized;

        bool staticMarkingsReady = markingsView != null && markingsView.TryGetCurrentTableReferenceGeometry(out _);
        bool staticMarkingsConfirmed = markingsView != null && markingsView.MarkingsFinalized;

        bool qrWorkflowAvailable = !isQrTemporarilyDisabled && qrController != null && qrController.EnableQrTrackingWorkflow;
        bool manualWorkflowRequired = isQrTemporarilyDisabled && manualAlignmentController != null && showManualAlignmentButtonWhenQrIsDisabled;

        bool manualAlignmentReadyForPocketConfirmation =
            !manualWorkflowRequired ||
            (manualAlignmentController.State == AlignmentState.EditingGeneratedPockets && manualAlignmentController.PythonFrameAligned);

        if (!environmentParsed)
        {
            SetStatusText(environmentParseStatusText, environmentWaitingMessage, waitingColor);
        }
        else if (tableService.IsLoadedFromBackup)
        {
            SetStatusText(environmentParseStatusText, environmentBackupLoadedMessage, partialColor);
        }
        else
        {
            SetStatusText(environmentParseStatusText, environmentLiveParsedMessage, readyColor);

            bool shouldInvokeParsedEvent = !_hasInvokedEnvironmentParsedEvent || !invokeParsedEventOnlyOnce;
            if (shouldInvokeParsedEvent)
            {
                _hasInvokedEnvironmentParsedEvent = true;
                OnEnvironmentParsed?.Invoke();
            }
        }

        if (!pocketsParsed)
        {
            if (manualWorkflowRequired && manualAlignmentController.State != AlignmentState.Idle)
            {
                SetStatusText(pocketParsingStatusText, manualAlignmentController.StatusText, partialColor);
            }
            else
            {
                SetStatusText(pocketParsingStatusText, pocketsWaitingMessage, waitingColor);
            }
        }
        else if (!pocketsConfirmed)
        {
            if (manualWorkflowRequired)
            {
                SetStatusText(
                    pocketParsingStatusText,
                    manualAlignmentController.StatusText,
                    manualAlignmentController.PythonFrameAligned ? readyColor : partialColor);
            }
            else if (qrWorkflowAvailable && qrController != null)
            {
                string pocketMessage = qrController.BuildPocketPlacementGuidanceMessage();
                Color pocketColor = (qrController.HasAppliedPocketPlacementFromQr || qrController.IsQrAlignmentLockedForSession) ? readyColor : partialColor;
                SetStatusText(pocketParsingStatusText, pocketMessage, pocketColor);
            }
            else
            {
                SetStatusText(pocketParsingStatusText, "Pocket data received. Adjust pockets if needed and confirm them.", readyColor);
            }
        }
        else
        {
            SetStatusText(pocketParsingStatusText, pocketsConfirmedMessage, readyColor);
        }

        if (!pocketsConfirmed)
        {
            SetStatusText(staticMarkingsStatusText, staticMarkingsWaitingMessage, waitingColor);
        }
        else if (!staticMarkingsReady)
        {
            SetStatusText(staticMarkingsStatusText, "Preparing static markings...", waitingColor);
        }
        else if (!staticMarkingsConfirmed)
        {
            SetStatusText(staticMarkingsStatusText, staticMarkingsAdjustMessage, partialColor);
        }
        else
        {
            SetStatusText(staticMarkingsStatusText, staticMarkingsConfirmedMessage, readyColor);
        }

        if (qrWorkflowAvailable && qrController != null)
        {
            string qrStatusMessage = qrController.BuildStatusMessage();

            if (forceRefresh || !string.Equals(_lastQrStatusMessage, qrStatusMessage, System.StringComparison.Ordinal))
            {
                SetStatusText(qrCodeScanStatusText, qrStatusMessage, qrController.GetSuggestedStatusColor());
                _lastQrStatusMessage = qrStatusMessage;
            }
        }
        else
        {
            string qrDisabledMessage = manualWorkflowRequired
                ? "QR workflow disabled. Use the manual two-anchor alignment workflow."
                : "QR workflow disabled.";

            SetStatusText(qrCodeScanStatusText, qrDisabledMessage, waitingColor);
            _lastQrStatusMessage = qrDisabledMessage;
        }

        bool qrRequirementSatisfied =
            !requireQrReadyBeforePocketConfirmation ||
            !qrWorkflowAvailable ||
            qrController == null ||
            qrController.IsReadyToPlacePockets ||
            tableService.DebugBypassQrAlignmentGate;

        bool canConfirmPocketPlacement =
            tableService.CanFinalizePocketPlacement() &&
            !pocketsConfirmed &&
            qrRequirementSatisfied &&
            manualAlignmentReadyForPocketConfirmation;

        bool canConfirmStaticMarkings = pocketsConfirmed && staticMarkingsReady && !staticMarkingsConfirmed;

        bool showQrButton = qrWorkflowAvailable && (!hideQrLockButtonAfterPocketConfirmation || !pocketsConfirmed);
        bool canLockQr = qrController != null && !pocketsConfirmed && qrController.CanLockCurrentDetections;

        string qrButtonLabel = qrController != null
            ? qrController.BuildLockButtonStatusLabel()
            : "QR unavailable";

        SetButtonState(
            confirmPocketPlacementButton,
            canConfirmPocketPlacement,
            pocketsConfirmed ? "Pockets confirmed" : "Confirm pockets");

        SetButtonState(
            confirmStaticMarkingsPlacementButton,
            canConfirmStaticMarkings,
            staticMarkingsConfirmed ? "Markings confirmed" : "Confirm markings");

        SetButtonVisible(lockQrPositionButton, showQrButton);
        SetButtonState(lockQrPositionButton, canLockQr, qrButtonLabel);

        bool showManualAlignmentButton = manualWorkflowRequired && !pocketsConfirmed;
        bool canAdvanceManualAlignment =
            showManualAlignmentButton &&
            environmentParsed &&
            !manualAlignmentController.PythonFrameAligned;

        string manualAlignmentButtonLabel = showManualAlignmentButton
            ? (manualAlignmentController.PythonFrameAligned ? "Alignment ready" : manualAlignmentController.NextActionButtonText)
            : "Begin placing";

        SetButtonVisible(advanceManualAlignmentButton, showManualAlignmentButton);
        SetButtonState(advanceManualAlignmentButton, canAdvanceManualAlignment, manualAlignmentButtonLabel);

        bool ballEditingSetupValid =
            pocketsConfirmed &&
            tableService.IsReadyToVisualizeBalls();

        if (forceHideBallEntryButtonsWhenSetupIsInvalid && !ballEditingSetupValid && BallOverrideSelectable.AreEntryButtonsGloballyVisible)
        {
            BallOverrideSelectable.SetGlobalEntryButtonsVisible(false);
            ManualBallOverrideService.Instance?.ClearSelection();
        }

        bool showBallEntryToggle = showBallEntryToggleAfterPocketConfirmation && pocketsConfirmed;
        bool canToggleBallEntryButtons =
            ballEditingSetupValid &&
            BallOverrideSelectable.RegisteredSelectableCount > 0;

        string ballEntryToggleLabel = BallOverrideSelectable.AreEntryButtonsGloballyVisible
            ? "Hide ball edit buttons"
            : "Show ball edit buttons";

        SetButtonVisible(toggleBallEntryButtonsButton, showBallEntryToggle);
        SetButtonState(toggleBallEntryButtonsButton, canToggleBallEntryButtons, ballEntryToggleLabel);
    }

    private TableStaticMarkingsView ResolveMarkingsView()
    {
        if (tableStaticMarkingsView != null)
            return tableStaticMarkingsView;

        tableStaticMarkingsView = FindFirstObjectByType<TableStaticMarkingsView>();
        return tableStaticMarkingsView;
    }

    private TableQrAlignmentController ResolveQrAlignmentController()
    {
        if (tableQrAlignmentController != null)
            return tableQrAlignmentController;

        tableQrAlignmentController = FindFirstObjectByType<TableQrAlignmentController>();
        return tableQrAlignmentController;
    }

    private TableManualAlignmentController ResolveManualAlignmentController()
    {
        if (tableManualAlignmentController != null)
            return tableManualAlignmentController;

        tableManualAlignmentController = FindFirstObjectByType<TableManualAlignmentController>();
        return tableManualAlignmentController;
    }

    private void HandleAdvanceManualAlignmentClicked()
    {
        TableManualAlignmentController manualAlignmentController = ResolveManualAlignmentController();

        if (manualAlignmentController == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Manual alignment button was pressed, but TableManualAlignmentController was not found.");
            return;
        }

        manualAlignmentController.AdvancePlacementStep();
        OnManualAlignmentAdvanced?.Invoke();
        RefreshUiFromTableService(forceRefresh: true);
    }

    private void HandleLockQrPositionClicked()
    {
        if (isQrTemporarilyDisabled)
        {
            Debug.Log("[EnvironmentStatusMenuController] QR workflow is temporarily disabled for this build.");
            return;
        }

        TableQrAlignmentController qrController = ResolveQrAlignmentController();

        if (qrController == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] QR lock requested, but TableQrAlignmentController was not found.");
            return;
        }

        bool applied = qrController.LockCurrentDetectionsAndApplyPockets();

        if (applied)
        {
            Debug.Log("[EnvironmentStatusMenuController] QR alignment applied and pockets were initialized from the QR snapshot.");
            OnQrLockedAndApplied?.Invoke();
        }
        else
        {
            Debug.Log("[EnvironmentStatusMenuController] QR state updated, but pocket placement was not applied.");
        }

        RefreshUiFromTableService(forceRefresh: true);
    }

    private void HandleConfirmPocketPlacementClicked()
    {
        TableService tableService = TableService.Instance;
        TableStaticMarkingsView markingsView = ResolveMarkingsView();
        TableQrAlignmentController qrController = ResolveQrAlignmentController();
        TableManualAlignmentController manualAlignmentController = ResolveManualAlignmentController();

        if (tableService == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets requested, but TableService.Instance is null.");
            return;
        }

        if (!tableService.CanFinalizePocketPlacement())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets requested before pocket placement was ready.");
            return;
        }

        if (!tableService.HasAllPockets())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets requested before all pocket positions were available.");
            return;
        }

        if (!isQrTemporarilyDisabled &&
            requireQrReadyBeforePocketConfirmation &&
            qrController != null &&
            qrController.EnableQrTrackingWorkflow &&
            !qrController.IsReadyToPlacePockets &&
            !tableService.DebugBypassQrAlignmentGate)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets requested before QR alignment was completed.");
            return;
        }

        if (isQrTemporarilyDisabled &&
            manualAlignmentController != null &&
            (manualAlignmentController.State != AlignmentState.EditingGeneratedPockets || !manualAlignmentController.PythonFrameAligned))
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets requested before the manual Python-to-Quest frame alignment step was completed.");
            return;
        }

        tableService.FinalizePocketPlacement();

        if (markingsView != null)
            markingsView.ReEnableStaticMarkingsEditing(resetToDeterministic: true);

        Debug.Log("[EnvironmentStatusMenuController] Pocket placement confirmed.");
        OnConfirmPocketPlacementRequested?.Invoke();

        if (disableConfirmButtonAfterClick)
            SetButtonState(confirmPocketPlacementButton, false, "Pockets confirmed");

        RefreshUiFromTableService(forceRefresh: true);
    }

    private void HandleConfirmStaticMarkingsPlacementClicked()
    {
        TableStaticMarkingsView markingsView = ResolveMarkingsView();

        if (markingsView == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm markings requested, but TableStaticMarkingsView was not found.");
            return;
        }

        if (!markingsView.CanFinalizeStaticMarkingsPlacement())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm markings requested before static markings were ready.");
            return;
        }

        markingsView.FinalizeStaticMarkingsPlacement();

        Debug.Log("[EnvironmentStatusMenuController] Static markings confirmed.");
        OnConfirmStaticMarkingsPlacementRequested?.Invoke();

        if (disableConfirmButtonAfterClick)
            SetButtonState(confirmStaticMarkingsPlacementButton, false, "Markings confirmed");

        RefreshUiFromTableService(forceRefresh: true);
    }

    private void HandleToggleBallEntryButtonsClicked()
    {
        TableService tableService = TableService.Instance;

        if (tableService == null)
            return;

        if (!tableService.IsReadyToVisualizeBalls())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Ball edit buttons cannot be toggled before the ball visualization gate is open.");
            return;
        }

        bool nextVisible = !BallOverrideSelectable.AreEntryButtonsGloballyVisible;

        BallOverrideSelectable.SetGlobalEntryButtonsVisible(nextVisible);

        if (!nextVisible)
        {
            ManualBallOverrideService.Instance?.ClearSelection();
            OnBallEntryButtonsHidden?.Invoke();
        }
        else
        {
            OnBallEntryButtonsShown?.Invoke();
        }

        RefreshUiFromTableService(forceRefresh: true);
    }

    private void SetButtonState(Button button, bool interactable, string label) // UPDATED: button label is resolved automatically from the button hierarchy
    {
        if (button != null)
            button.interactable = interactable;

        TextMeshProUGUI labelTarget = GetButtonLabel(button);
        if (labelTarget == null)
            return;

        labelTarget.text = label;
        labelTarget.color = interactable ? enabledButtonLabelColor : disabledButtonLabelColor;
    }

    private static TextMeshProUGUI GetButtonLabel(Button button) // UPDATED: central label lookup instead of serialized child-text references
    {
        if (button == null)
            return null;

        return button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
    }

    private void SetButtonVisible(Button button, bool visible)
    {
        if (button == null)
            return;

        if (button.gameObject.activeSelf != visible)
            button.gameObject.SetActive(visible);
    }

    private void SetStatusText(TextMeshProUGUI target, string message, Color color)
    {
        if (target == null)
            return;

        target.text = message;
        target.color = color;
    }
}