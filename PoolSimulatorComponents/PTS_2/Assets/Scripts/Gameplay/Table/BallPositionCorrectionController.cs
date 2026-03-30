using System;
using UnityEngine;

public class BallPositionCorrectionController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private ManualBallOverrideService manualBallOverrideServiceOverride;
    [SerializeField] private TableService tableServiceOverride;

    [Header("Original Detected Position Preview")]
    [SerializeField] private Transform originalDetectedPositionPreviewParent;
    [SerializeField] private Material originalDetectedPositionPreviewMaterial;
    [SerializeField] private float originalDetectedPositionPreviewLiftM = 0.003f;
    [SerializeField] private float originalDetectedPositionPreviewScaleMultiplier = 1.03f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private ManualBallOverrideService _manualBallOverrideService;
    private TableService _tableService;
    private GameObject _originalDetectedPositionPreviewInstance;

    public string LastStatusMessage { get; private set; } = string.Empty;

    public bool HasSelectedBall =>
        _manualBallOverrideService != null &&
        _manualBallOverrideService.SelectedBall != null;

    public bool IsPositionCorrectionActive =>
        _manualBallOverrideService != null &&
        _manualBallOverrideService.IsSelectedBallInPositionCorrectionMode;

    public bool IsShowingOriginalDetectedPositionPreview { get; private set; }

    public bool CanToggleOriginalDetectedPositionPreview =>
        _manualBallOverrideService != null &&
        _manualBallOverrideService.SelectedBall != null &&
        _manualBallOverrideService.SelectedBall.HasDetectedBaseline() &&
        _manualBallOverrideService.SelectedBall.IsPositionUserOverriden();

    public bool CanRevertSelectedBallToDetectedPosition =>
        _manualBallOverrideService != null &&
        _manualBallOverrideService.SelectedBall != null &&
        _manualBallOverrideService.SelectedBall.IsPositionUserOverriden();

    public byte XAxisCorrectionStepMM =>
        AppSettings.Instance.Settings.GetXAxisCorrectionStepMM();

    public byte ZAxisCorrectionStepMM =>
        AppSettings.Instance.Settings.GetZAxisCorrectionStepMM();

    public float XAxisCorrectionStepM => XAxisCorrectionStepMM / 1000f;

    public float ZAxisCorrectionStepM => ZAxisCorrectionStepMM / 1000f;

    public event Action StateChanged;

    private void Awake() => ResolveDependencies();

    private void OnEnable()
    {
        ResolveDependencies();

        if (_manualBallOverrideService != null)
        {
            _manualBallOverrideService.SelectedBallChanged += HandleSelectedBallChanged;
            _manualBallOverrideService.SelectedBallPositionCorrectionModeChanged += HandleSelectedBallPositionCorrectionModeChanged;
        }
    }

    private void OnDisable()
    {
        if (_manualBallOverrideService != null)
        {
            _manualBallOverrideService.SelectedBallChanged -= HandleSelectedBallChanged;
            _manualBallOverrideService.SelectedBallPositionCorrectionModeChanged -= HandleSelectedBallPositionCorrectionModeChanged;
        }
    }

    private void LateUpdate()
    {
        if (IsShowingOriginalDetectedPositionPreview)
            UpdateOriginalDetectedPositionPreviewPose();
    }

    public string GetXAxisCorrectionStepDisplay() =>
        GameplayControlSettings.ToDisplayValue(XAxisCorrectionStepMM);

    public string GetZAxisCorrectionStepDisplay() =>
        GameplayControlSettings.ToDisplayValue(ZAxisCorrectionStepMM);

    public void BeginSelectedBallPositionCorrection()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
        {
            SetStatus("Manual ball override service is not available.", logAsWarning: true);
            return;
        }

        if (_manualBallOverrideService.BeginSelectedPositionCorrection(out string statusMessage))
        {
            SetStatus(statusMessage);
            return;
        }

        SetStatus(statusMessage, logAsWarning: true);
    }

    public void ConfirmSelectedBallPositionCorrection()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
        {
            SetStatus("Manual ball override service is not available.", logAsWarning: true);
            return;
        }

        HideOriginalDetectedPositionPreview();
        _manualBallOverrideService.ConfirmSelectedPositionCorrection();
        SetStatus("Ball position correction confirmed.");
    }

    public void CancelSelectedBallPositionCorrection()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
        {
            SetStatus("Manual ball override service is not available.", logAsWarning: true);
            return;
        }

        HideOriginalDetectedPositionPreview();
        _manualBallOverrideService.CancelSelectedPositionCorrection();
        SetStatus("Ball position correction cancelled. Previous position state restored.");
    }

    public void ReleaseSelectedBallPositionOverride()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
        {
            SetStatus("Manual ball override service is not available.", logAsWarning: true);
            return;
        }

        HideOriginalDetectedPositionPreview();
        _manualBallOverrideService.ReleaseSelectedPositionOverride();
        SetStatus("Ball position now follows Python App stream.");
    }

    public void RevertSelectedBallToDetectedPosition()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null || _manualBallOverrideService.SelectedBall == null)
        {
            SetStatus("No ball is currently selected.", logAsWarning: true);
            return;
        }

        if (!_manualBallOverrideService.SelectedBall.IsPositionUserOverriden())
        {
            HideOriginalDetectedPositionPreview();
            SetStatus("Selected ball already follows Python App stream.");
            return;
        }

        HideOriginalDetectedPositionPreview();
        _manualBallOverrideService.ReleaseSelectedPositionOverride();
        SetStatus("Selected ball reverted to the original detected position.");
    }

    public void ToggleOriginalDetectedPositionPreview()
    {
        ResolveDependencies();

        if (!CanToggleOriginalDetectedPositionPreview)
        {
            HideOriginalDetectedPositionPreview();
            SetStatus("Original detected position preview is not available.", logAsWarning: true);
            return;
        }

        if (IsShowingOriginalDetectedPositionPreview)
        {
            HideOriginalDetectedPositionPreview();
            SetStatus("Original detected position preview hidden.");
            return;
        }

        EnsureOriginalDetectedPositionPreviewInstance();
        UpdateOriginalDetectedPositionPreviewPose();

        IsShowingOriginalDetectedPositionPreview = true;

        if (_originalDetectedPositionPreviewInstance != null)
            _originalDetectedPositionPreviewInstance.SetActive(true);

        SetStatus("Original detected position preview shown.");
    }

    public void HideOriginalDetectedPositionPreview()
    {
        IsShowingOriginalDetectedPositionPreview = false;

        if (_originalDetectedPositionPreviewInstance != null)
            _originalDetectedPositionPreviewInstance.SetActive(false);

        StateChanged?.Invoke();
    }

    public void NudgeSelectedBallNegativeX() => NudgeSelectedBallOnTableAxes(-1, 0);

    public void NudgeSelectedBallPositiveX() => NudgeSelectedBallOnTableAxes(+1, 0);

    public void NudgeSelectedBallNegativeZ() => NudgeSelectedBallOnTableAxes(0, -1);

    public void NudgeSelectedBallPositiveZ() => NudgeSelectedBallOnTableAxes(0, +1);

    private void NudgeSelectedBallOnTableAxes(int xDirection, int zDirection)
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
        {
            SetStatus("Manual ball override service is not available.", logAsWarning: true);
            return;
        }

        if (_tableService == null)
        {
            SetStatus("Table service is not available.", logAsWarning: true);
            return;
        }

        if (!_manualBallOverrideService.IsSelectedBallInPositionCorrectionMode)
        {
            SetStatus("Ball position correction mode is not active.", logAsWarning: true);
            return;
        }

        Ball selectedBall = _manualBallOverrideService.SelectedBall;
        if (selectedBall == null)
        {
            SetStatus("No ball is currently selected.", logAsWarning: true);
            return;
        }

        if (!TryGetSelectedBallWorldPosition(selectedBall, out Vector3 currentWorldPosition))
        {
            SetStatus("Could not resolve the selected ball position.", logAsWarning: true);
            return;
        }

        if (!_tableService.TryGetBallCorrectionAxes(out Vector3 xAxis, out Vector3 zAxis))
        {
            SetStatus("Table correction axes are not ready yet.", logAsWarning: true);
            return;
        }

        Vector3 requestedWorldPosition =
            currentWorldPosition +
            (xAxis * (XAxisCorrectionStepM * xDirection)) +
            (zAxis * (ZAxisCorrectionStepM * zDirection));

        if (_manualBallOverrideService.TryMoveSelectedBallToWorldPosition(requestedWorldPosition, out string statusMessage))
        {
            SetStatus(statusMessage);
            return;
        }

        SetStatus(statusMessage, logAsWarning: true);
    }

    private void ResolveDependencies()
    {
        _manualBallOverrideService = manualBallOverrideServiceOverride != null
            ? manualBallOverrideServiceOverride
            : ManualBallOverrideService.Instance;

        _tableService = tableServiceOverride != null
            ? tableServiceOverride
            : TableService.Instance;
    }

    private bool TryGetSelectedBallWorldPosition(Ball selectedBall, out Vector3 selectedBallWorldPosition)
    {
        selectedBallWorldPosition = Vector3.zero;

        if (selectedBall == null)
            return false;

        if (_tableService == null || !_tableService.TryGetBallRestingWorldY(out float ballRestingY))
            return false;

        Vector2Float effectivePosition = selectedBall.GetEffectivePosition();
        if (effectivePosition == null)
            return false;

        selectedBallWorldPosition = new Vector3(effectivePosition.X, ballRestingY, effectivePosition.Y);
        return true;
    }

    private void EnsureOriginalDetectedPositionPreviewInstance()
    {
        if (_originalDetectedPositionPreviewInstance != null)
            return;

        Transform parent = originalDetectedPositionPreviewParent != null
            ? originalDetectedPositionPreviewParent
            : transform;

        _originalDetectedPositionPreviewInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _originalDetectedPositionPreviewInstance.name = "OriginalDetectedPositionPreview";
        _originalDetectedPositionPreviewInstance.transform.SetParent(parent, worldPositionStays: true);

        Collider collider = _originalDetectedPositionPreviewInstance.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;

        Renderer renderer = _originalDetectedPositionPreviewInstance.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (originalDetectedPositionPreviewMaterial != null)
            {
                renderer.sharedMaterial = originalDetectedPositionPreviewMaterial;
            }
            else
            {
                Material fallbackMaterial = new(renderer.sharedMaterial);
                fallbackMaterial.color = new Color(0f, 1f, 1f, 0.4f);
                renderer.sharedMaterial = fallbackMaterial;
            }
        }

        _originalDetectedPositionPreviewInstance.SetActive(false);
    }

    private void UpdateOriginalDetectedPositionPreviewPose()
    {
        if (!IsShowingOriginalDetectedPositionPreview)
            return;

        if (_manualBallOverrideService == null || _manualBallOverrideService.SelectedBall == null)
        {
            HideOriginalDetectedPositionPreview();
            return;
        }

        Ball selectedBall = _manualBallOverrideService.SelectedBall;

        if (!selectedBall.HasDetectedBaseline() || selectedBall.DetectedPosition == null)
        {
            HideOriginalDetectedPositionPreview();
            return;
        }

        if (_tableService == null || !_tableService.TryGetBallRestingWorldY(out float ballRestingY))
        {
            HideOriginalDetectedPositionPreview();
            return;
        }

        EnsureOriginalDetectedPositionPreviewInstance();

        Vector3 worldPosition = new(
            selectedBall.DetectedPosition.X,
            ballRestingY + originalDetectedPositionPreviewLiftM,
            selectedBall.DetectedPosition.Y);

        float ballDiameterM = _tableService != null && _tableService.BallDiameterM > 0f
            ? _tableService.BallDiameterM
            : 0.05715f;

        _originalDetectedPositionPreviewInstance.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        _originalDetectedPositionPreviewInstance.transform.localScale =
            Vector3.one * (ballDiameterM * originalDetectedPositionPreviewScaleMultiplier);

        if (!_originalDetectedPositionPreviewInstance.activeSelf)
            _originalDetectedPositionPreviewInstance.SetActive(true);
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable _)
    {
        LastStatusMessage = string.Empty;
        HideOriginalDetectedPositionPreview();
        StateChanged?.Invoke();
    }

    private void HandleSelectedBallPositionCorrectionModeChanged(BallOverrideSelectable _, bool isActive)
    {
        if (!isActive)
            HideOriginalDetectedPositionPreview();

        StateChanged?.Invoke();
    }

    private void SetStatus(string message, bool logAsWarning = false)
    {
        LastStatusMessage = message ?? string.Empty;

        if (verboseLogs && !string.IsNullOrWhiteSpace(LastStatusMessage))
        {
            if (logAsWarning)
                Debug.LogWarning($"[BallPositionCorrectionController] {LastStatusMessage}");
            else
                Debug.Log($"[BallPositionCorrectionController] {LastStatusMessage}");
        }

        StateChanged?.Invoke();
    }
}