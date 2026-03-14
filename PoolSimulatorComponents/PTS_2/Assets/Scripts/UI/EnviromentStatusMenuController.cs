using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class EnvironmentStatusMenuController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI environmentParseStatusText;
    [SerializeField] private TextMeshProUGUI pocketParsingStatusText;
    [SerializeField] private TextMeshProUGUI staticMarkingsStatusText; // ADDED
    [SerializeField] private TextMeshProUGUI qrCodeScanStatusText;

    [SerializeField] private Button confirmPocketPlacementButton;
    [SerializeField] private TextMeshProUGUI confirmPocketPlacementButtonText;

    [SerializeField] private Button confirmStaticMarkingsPlacementButton; // ADDED
    [SerializeField] private TextMeshProUGUI confirmStaticMarkingsPlacementButtonText; // ADDED

    [Header("Scene References")]
    [SerializeField] private TableStaticMarkingsView tableStaticMarkingsView; // ADDED

    [Header("Status Messages")]
    [TextArea]
    [SerializeField] private string environmentWaitingMessage = "Waiting for environment data to be parsed...";
    [TextArea]
    [SerializeField] private string environmentParsedMessage = "Environment successfully parsed.";
    [TextArea]
    [SerializeField] private string pocketsWaitingMessage = "Waiting for pockets to be loaded...";
    [TextArea]
    [SerializeField] private string pocketsParsedMessage = "All pocket positions loaded successfully.";
    [TextArea]
    [SerializeField] private string pocketsConfirmedMessage = "Pocket placement confirmed."; // ADDED
    [TextArea]
    [SerializeField] private string staticMarkingsWaitingMessage = "Confirm pocket placement to show the 1/4 line and rack guide."; // ADDED
    [TextArea]
    [SerializeField] private string staticMarkingsAdjustMessage = "Adjust the 1/4 line and rack guide, then confirm static markings."; // ADDED
    [TextArea]
    [SerializeField] private string staticMarkingsConfirmedMessage = "Static markings confirmed."; // ADDED
    [TextArea]
    [SerializeField] private string qrDeferredMessage = "QR code refinement deferred.";
    [TextArea]
    [SerializeField] private string qrCompletedMessage = "QR code refinement completed.";

    // Rearange multiple booleans into one 
    [Header("Behavior")]
    [SerializeField] private bool _disableConfirmButtonAfterClick = true;
    [SerializeField] private bool _invokeParsedEventOnlyOnce = true;

    [Header("Events")]
    public UnityEvent OnEnvironmentParsed;
    public UnityEvent OnConfirmPocketPlacementRequested;
    public UnityEvent OnConfirmStaticMarkingsPlacementRequested; // ADDED

    private bool _environmentParsedLastFrame;
    private bool _pocketsParsedLastFrame;
    private bool _pocketsConfirmedLastFrame; // ADDED
    private bool _staticMarkingsConfirmedLastFrame; // ADDED
    private bool _qrParsedLastFrame;
    private bool _hasInvokedEnvironmentParsedEvent;

    private void Awake()
    {
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
    }

    private void OnEnable()
    {
        RefreshUiFromTableService(forceRefresh: true);
    }

    private void Update()
    {
        RefreshUiFromTableService(forceRefresh: false);
    }

    private void InitializeUiDefaults()
    {
        SetStatusText(environmentParseStatusText, environmentWaitingMessage, Color.white);
        SetStatusText(pocketParsingStatusText, pocketsWaitingMessage, Color.white);
        SetStatusText(staticMarkingsStatusText, staticMarkingsWaitingMessage, Color.white);
        SetStatusText(qrCodeScanStatusText, qrDeferredMessage, Color.white);

        SetButtonState(confirmPocketPlacementButton, confirmPocketPlacementButtonText, false, "Confirm pockets");
        SetButtonState(confirmStaticMarkingsPlacementButton, confirmStaticMarkingsPlacementButtonText, false, "Confirm markings");
    }

    private void RefreshUiFromTableService(bool forceRefresh)
    {
        TableService tableService = TableService.Instance;
        TableStaticMarkingsView markingsView = ResolveMarkingsView();

        if (tableService == null)
        {
            if (forceRefresh)
            {
                SetStatusText(environmentParseStatusText, environmentWaitingMessage, Color.white);
                SetStatusText(pocketParsingStatusText, pocketsWaitingMessage, Color.white);
                SetStatusText(staticMarkingsStatusText, staticMarkingsWaitingMessage, Color.white);
                SetStatusText(qrCodeScanStatusText, qrDeferredMessage, Color.white);

                SetButtonState(confirmPocketPlacementButton, confirmPocketPlacementButtonText, false, "Confirm pockets");
                SetButtonState(confirmStaticMarkingsPlacementButton, confirmStaticMarkingsPlacementButtonText, false, "Confirm markings");
            }

            return;
        }

        bool environmentParsed = tableService.ArePropertiesParsed();
        bool pocketsParsed = tableService.HasAllPockets();
        bool pocketsConfirmed = tableService.LockFinalized; // ADDED

        bool staticMarkingsReady = markingsView != null && markingsView.TryGetCurrentTableReferenceGeometry(out _);
        bool staticMarkingsConfirmed = markingsView != null && markingsView.MarkingsFinalized; // ADDED

        bool qrParsed = false; // QR deferred intentionally for now

        if (forceRefresh || environmentParsed != _environmentParsedLastFrame)
        {
            if (environmentParsed)
            {
                SetStatusText(environmentParseStatusText, environmentParsedMessage, Color.green);

                bool shouldInvokeParsedEvent = !_hasInvokedEnvironmentParsedEvent || !_invokeParsedEventOnlyOnce;
                if (shouldInvokeParsedEvent)
                {
                    _hasInvokedEnvironmentParsedEvent = true;
                    OnEnvironmentParsed?.Invoke();
                }
            }
            else
            {
                SetStatusText(environmentParseStatusText, environmentWaitingMessage, Color.white);
            }

            _environmentParsedLastFrame = environmentParsed;
        }

        if (forceRefresh || pocketsParsed != _pocketsParsedLastFrame || pocketsConfirmed != _pocketsConfirmedLastFrame)
        {
            if (!pocketsParsed)
            {
                string message = $"Waiting for pockets to be loaded... ({tableService.PocketCount}/{tableService.MAX_POCKET_COUNT})";
                SetStatusText(pocketParsingStatusText, message, Color.white);
            }
            else if (!pocketsConfirmed)
            {
                SetStatusText(pocketParsingStatusText, pocketsParsedMessage, Color.green);
            }
            else
            {
                SetStatusText(pocketParsingStatusText, pocketsConfirmedMessage, Color.green);
            }

            _pocketsParsedLastFrame = pocketsParsed;
            _pocketsConfirmedLastFrame = pocketsConfirmed;
        }

        if (forceRefresh || staticMarkingsConfirmed != _staticMarkingsConfirmedLastFrame || pocketsConfirmed != _pocketsConfirmedLastFrame)
        {
            if (!pocketsConfirmed)
            {
                SetStatusText(staticMarkingsStatusText, staticMarkingsWaitingMessage, Color.white);
            }
            else if (!staticMarkingsReady)
            {
                SetStatusText(staticMarkingsStatusText, "Preparing static markings...", Color.white);
            }
            else if (!staticMarkingsConfirmed)
            {
                SetStatusText(staticMarkingsStatusText, staticMarkingsAdjustMessage, Color.yellow);
            }
            else
            {
                SetStatusText(staticMarkingsStatusText, staticMarkingsConfirmedMessage, Color.green);
            }

            _staticMarkingsConfirmedLastFrame = staticMarkingsConfirmed;
        }

        if (forceRefresh || qrParsed != _qrParsedLastFrame)
        {
            if (qrParsed)
            {
                SetStatusText(qrCodeScanStatusText, qrCompletedMessage, Color.green);
            }
            else
            {
                SetStatusText(qrCodeScanStatusText, qrDeferredMessage, Color.white);
            }

            _qrParsedLastFrame = qrParsed;
        }

        bool canConfirmPocketPlacement = environmentParsed && pocketsParsed && !pocketsConfirmed;
        bool canConfirmStaticMarkings = pocketsConfirmed && staticMarkingsReady && !staticMarkingsConfirmed;

        SetButtonState(
            confirmPocketPlacementButton,
            confirmPocketPlacementButtonText,
            canConfirmPocketPlacement,
            pocketsConfirmed ? "Pockets confirmed" : "Confirm pockets");

        SetButtonState(
            confirmStaticMarkingsPlacementButton,
            confirmStaticMarkingsPlacementButtonText,
            canConfirmStaticMarkings,
            staticMarkingsConfirmed ? "Markings confirmed" : "Confirm markings");
    }

    private TableStaticMarkingsView ResolveMarkingsView()
    {
        if (tableStaticMarkingsView != null)
            return tableStaticMarkingsView;

        tableStaticMarkingsView = FindFirstObjectByType<TableStaticMarkingsView>();
        return tableStaticMarkingsView;
    }

    private void HandleConfirmPocketPlacementClicked()
    {
        TableService tableService = TableService.Instance;
        TableStaticMarkingsView markingsView = ResolveMarkingsView();

        if (tableService == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets pressed, but TableService.Instance is null.");
            return;
        }

        if (!tableService.ArePropertiesParsed())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets pressed before environment parsing completed.");
            return;
        }

        if (!tableService.HasAllPockets())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pockets pressed before all pocket positions were loaded.");
            return;
        }

        tableService.FinalizePocketPlacement();

        // ADDED: after pocket confirmation, enable the static-markings editing phase.
        if (markingsView != null)
        {
            markingsView.ReEnableStaticMarkingsEditing(resetToDeterministic: true);
        }

        Debug.Log("[EnvironmentStatusMenuController] Pocket placement confirmed.");
        OnConfirmPocketPlacementRequested?.Invoke();

        if (_disableConfirmButtonAfterClick)
        {
            SetButtonState(confirmPocketPlacementButton, confirmPocketPlacementButtonText, false, "Pockets confirmed");
        }
    }

    private void HandleConfirmStaticMarkingsPlacementClicked()
    {
        TableStaticMarkingsView markingsView = ResolveMarkingsView();

        if (markingsView == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm markings pressed, but TableStaticMarkingsView was not found.");
            return;
        }

        if (!markingsView.CanFinalizeStaticMarkingsPlacement())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm markings pressed before the static markings were ready.");
            return;
        }

        markingsView.FinalizeStaticMarkingsPlacement();

        Debug.Log("[EnvironmentStatusMenuController] Static markings confirmed.");
        OnConfirmStaticMarkingsPlacementRequested?.Invoke();

        if (_disableConfirmButtonAfterClick)
        {
            SetButtonState(confirmStaticMarkingsPlacementButton, confirmStaticMarkingsPlacementButtonText, false, "Markings confirmed");
        }
    }

    private void SetButtonState(Button button, TextMeshProUGUI labelTarget, bool interactable, string label)
    {
        if (button != null)
        {
            button.interactable = interactable;
        }

        if (labelTarget != null)
        {
            labelTarget.text = label;
            labelTarget.color = interactable ? Color.green : Color.white;
        }
    }

    private void SetStatusText(TextMeshProUGUI target, string message, Color color)
    {
        if (target == null)
            return;

        target.text = message;
        target.color = color;
    }
}