using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class EnvironmentStatusMenuController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI environmentParseStatusText; // e.g. Environment successfully parsed.
    [SerializeField] private TextMeshProUGUI pocketParsingStatusText;    // e.g. All 6 pockets loaded.
    [SerializeField] private TextMeshProUGUI qrCodeScanStatusText;       // e.g. QR refinement deferred.
    [SerializeField] private Button confirmPocketPlacementButton;
    [SerializeField] private TextMeshProUGUI confirmPocketPlacementButtonText;

    [Header("Status Messages")]
    [TextArea]
    [SerializeField] private string environmentWaitingMessage = "Waiting for environment data to be parsed...";
    [TextArea]
    [SerializeField] private string environmentParsedMessage = "Environment successfully parsed."; // e.g. parsed environment
    [TextArea]
    [SerializeField] private string pocketsWaitingMessage = "Waiting for pockets to be loaded...";
    [TextArea]
    [SerializeField] private string pocketsParsedMessage = "All pocket positions loaded successfully.";
    [TextArea]
    [SerializeField] private string qrDeferredMessage = "QR code refinement deferred.";
    [TextArea]
    [SerializeField] private string qrCompletedMessage = "QR code refinement completed.";

    [Header("Behavior")]
    [SerializeField] private bool disableConfirmButtonAfterClick = true;
    [SerializeField] private bool invokeParsedEventOnlyOnce = true;

    [Header("Events")]
    public UnityEvent OnEnvironmentParsed; // Hook other systems here in Inspector if needed.
    public UnityEvent OnConfirmPocketPlacementRequested; // Hook confirm workflow here.

    private bool _environmentParsedLastFrame;
    private bool _pocketsParsedLastFrame;
    private bool _qrParsedLastFrame;
    private bool _hasInvokedEnvironmentParsedEvent;

    private void Awake()
    {
        InitializeUiDefaults(); // UPDATED: ensure the panel is always valid on scene load

        if (confirmPocketPlacementButton != null)
        {
            confirmPocketPlacementButton.onClick.RemoveListener(HandleConfirmPocketPlacementClicked); // UPDATED: avoid duplicate listeners
            confirmPocketPlacementButton.onClick.AddListener(HandleConfirmPocketPlacementClicked);    // UPDATED: clean button wiring
        }
    }

    private void OnEnable()
    {
        RefreshUiFromTableService(forceRefresh: true); // UPDATED: refresh immediately when object becomes active
    }

    private void Update()
    {
        RefreshUiFromTableService(forceRefresh: false);
    }

    private void InitializeUiDefaults()
    {
        SetStatusText(environmentParseStatusText, environmentWaitingMessage, Color.white);
        SetStatusText(pocketParsingStatusText, pocketsWaitingMessage, Color.white);
        SetStatusText(qrCodeScanStatusText, qrDeferredMessage, Color.white);

        SetConfirmButtonState(false, "Confirm pockets");
    }

    private void RefreshUiFromTableService(bool forceRefresh)
    {
        TableService tableService = TableService.Instance;

        if (tableService == null)
        {
            if (forceRefresh)
            {
                SetStatusText(environmentParseStatusText, "Waiting for environment data to be parsed...", Color.white);
                SetStatusText(pocketParsingStatusText, "Waiting for pocket positions to be loaded...", Color.white);
                SetStatusText(qrCodeScanStatusText, "QR code refinement deferred.", Color.white);
                SetConfirmButtonState(false, "Confirm pockets");
            }

            return;
        }

        bool environmentParsed = tableService.ArePropertiesParsed();
        bool pocketsParsed = tableService.HasAllPockets();

        // QR is intentionally deferred for now.
        bool qrParsed = false; // UPDATED: QR work intentionally postponed in current flow

        if (forceRefresh || environmentParsed != _environmentParsedLastFrame)
        {
            if (environmentParsed)
            {
                SetStatusText(environmentParseStatusText, environmentParsedMessage, Color.green);

                bool shouldInvokeParsedEvent = !_hasInvokedEnvironmentParsedEvent || !invokeParsedEventOnlyOnce;
                if (shouldInvokeParsedEvent)
                {
                    _hasInvokedEnvironmentParsedEvent = true; // UPDATED: one-shot event gate
                    OnEnvironmentParsed?.Invoke();            // UPDATED: fire only when parsed is truly valid
                }
            }
            else
            {
                SetStatusText(environmentParseStatusText, environmentWaitingMessage, Color.white);
            }

            _environmentParsedLastFrame = environmentParsed;
        }

        if (forceRefresh || pocketsParsed != _pocketsParsedLastFrame)
        {
            if (pocketsParsed)
            {
                SetStatusText(pocketParsingStatusText, pocketsParsedMessage, Color.green);
            }
            else
            {
                string message = $"Waiting for pockets to be loaded... ({tableService.PocketCount}/{tableService.MAX_POCKET_COUNT})"; // e.g. pocket count progress
                SetStatusText(pocketParsingStatusText, message, Color.white);
            }

            _pocketsParsedLastFrame = pocketsParsed;
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

        bool canConfirmPocketPlacement = environmentParsed && pocketsParsed;
        SetConfirmButtonState(canConfirmPocketPlacement, canConfirmPocketPlacement ? "Confirm pockets" : "Confirm pockets");
    }

    private void HandleConfirmPocketPlacementClicked()
    {
        TableService tableService = TableService.Instance;

        if (tableService == null)
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pressed, but TableService.Instance is null.");
            return;
        }

        if (!tableService.ArePropertiesParsed())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pressed before environment parsing completed.");
            return;
        }

        if (!tableService.HasAllPockets())
        {
            Debug.LogWarning("[EnvironmentStatusMenuController] Confirm pressed before all pocket positions were loaded.");
            return;
        }

        tableService.FinalizePocketPlacement(); // UPDATED: call the single authoritative freeze method

        Debug.Log("[EnvironmentStatusMenuController] Pocket placement confirmed.");

        OnConfirmPocketPlacementRequested?.Invoke();

        if (disableConfirmButtonAfterClick)
        {
            SetConfirmButtonState(false, "Pockets confirmed");
        }
    }



    private void SetConfirmButtonState(bool interactable, string label)
    {
        if (confirmPocketPlacementButton != null)
        {
            confirmPocketPlacementButton.interactable = interactable;
        }

        if (confirmPocketPlacementButtonText != null)
        {
            confirmPocketPlacementButtonText.text = label;
            confirmPocketPlacementButtonText.color = interactable ? Color.green : Color.white;
        }
    }

    private void SetStatusText(TextMeshProUGUI target, string message, Color color)
    {
        if (target == null)
        {
            return;
        }

        target.text = message;
        target.color = color;
    }
}