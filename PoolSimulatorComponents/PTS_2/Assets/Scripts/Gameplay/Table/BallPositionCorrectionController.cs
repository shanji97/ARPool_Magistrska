using System;
using UnityEngine;

// Attach to: GameplaySystems/BallPositionCorrectionController in PoolSetup scene
public class BallPositionCorrectionController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private ManualBallOverrideService manualBallOverrideServiceOverride;
    [SerializeField] private TableService tableServiceOverride;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private ManualBallOverrideService _manualBallOverrideService;
    private TableService _tableService;

    public string LastStatusMessage { get; private set; } = string.Empty;

    public bool HasSelectedBall =>
        _manualBallOverrideService != null &&
        _manualBallOverrideService.SelectedBall != null;

    public bool IsPositionCorrectionActive =>
        _manualBallOverrideService != null &&
        _manualBallOverrideService.IsSelectedBallInPositionCorrectionMode;

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

        _manualBallOverrideService.ReleaseSelectedPositionOverride();
        SetStatus("Ball position now follows Python App stream.");
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

    private void HandleSelectedBallChanged(BallOverrideSelectable _)
    {
        LastStatusMessage = string.Empty;
        StateChanged?.Invoke();
    }

    private void HandleSelectedBallPositionCorrectionModeChanged(BallOverrideSelectable _, bool __)
    {
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