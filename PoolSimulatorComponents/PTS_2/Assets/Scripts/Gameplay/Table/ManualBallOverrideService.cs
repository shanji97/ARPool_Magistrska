// Attach to: GameplaySystems/ManualBallOverrideService in PoolSetup scene
using System;
using UnityEngine;

public class ManualBallOverrideService : MonoBehaviour
{
    public static ManualBallOverrideService Instance { get; private set; }

    public BallOverrideSelectable SelectedSelectable { get; private set; }

    public Ball SelectedBall => SelectedSelectable != null ? SelectedSelectable.RuntimeBall : null;

    public bool HasSelection => SelectedBall != null;

    public bool IsSelectedBallInPositionCorrectionMode { get; private set; }

    public event Action<BallOverrideSelectable> SelectedBallChanged;
    public event Action<BallOverrideSelectable, bool> SelectedBallPositionCorrectionModeChanged;

    private Vector2Float _selectedBallPositionBeforeCorrection;
    private bool _selectedBallHadPositionOverrideBeforeCorrection;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public void SelectBall(BallOverrideSelectable selectable)
    {
        if (selectable == null || selectable.RuntimeBall == null)
            return;

        if (SelectedSelectable != selectable)
            EndSelectedPositionCorrectionInternal(revertToOriginalPosition: true);

        SelectedSelectable = selectable;
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ClearSelection()
    {
        EndSelectedPositionCorrectionInternal(revertToOriginalPosition: true);
        SelectedSelectable = null;
        SelectedBallChanged?.Invoke(null);
    }

    public void ApplySelectedTypeOverride(BallType newType)
    {
        _ = TryApplySelectedTypeOverrideOption(MapBallTypeToMenuOption(newType), out _);
    }

    public bool TryApplySelectedTypeOverrideOption(BallTypeOverrideMenuOption option, out string statusMessage)
    {
        if (SelectedBall == null)
        {
            statusMessage = "No ball is currently selected.";
            return false;
        }

        if (option == BallTypeOverrideMenuOption.FromPythonStream)
        {
            ReleaseSelectedTypeOverride();
            statusMessage = "Type now follows Python App stream.";
            return true;
        }

        BallType targetType = MapMenuOptionToBallType(option);

        if (IsSpecialBallType(targetType) && HasConflictingSpecialBall(targetType))
        {
            statusMessage = targetType == BallType.Cue
                ? "Another cue ball already exists."
                : "Another eightball already exists.";

            return false;
        }

        if (!TryResolveBallIdForTargetType(SelectedBall, targetType, out byte resolvedBallId))
        {
            statusMessage = targetType == BallType.Solid
                ? "No free solid ball number is available."
                : targetType == BallType.Stripe
                    ? "No free striped ball number is available."
                    : "Could not resolve a valid ball number.";

            return false;
        }

        SelectedBall.OverrideBallType(targetType);
        SelectedBall.AssignBallId(resolvedBallId);

        SelectedBallChanged?.Invoke(SelectedSelectable);

        statusMessage = targetType switch
        {
            BallType.Cue => "Cue override applied.",
            BallType.Eight => "Eightball override applied.",
            BallType.Solid => $"Solid override applied with number {resolvedBallId}.",
            BallType.Stripe => $"Striped override applied with number {resolvedBallId}.",
            _ => "Type override applied."
        };

        return true;
    }

    public void ApplySelectedBallIdOverride(int newBallId)
    {
        if (SelectedBall == null)
            return;

        SelectedBall.OverrideBallId((byte)Mathf.Clamp(newBallId, 0, 15));
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void SetSelectedIgnoredState(bool isIgnored)
    {
        if (SelectedBall == null)
            return;

        SelectedBall.SetIgnoredByUser(isIgnored);
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ToggleSelectedIgnoredState()
    {
        if (SelectedBall == null)
            return;

        SetSelectedIgnoredState(!SelectedBall.IsIgnoredByUser());
    }

    public bool BeginSelectedPositionCorrection(out string statusMessage)
    {
        if (SelectedBall == null)
        {
            statusMessage = "No ball is currently selected.";
            return false;
        }

        if (TableService.Instance == null || !TableService.Instance.TryGetBallRestingWorldY(out _))
        {
            statusMessage = "Table geometry is not ready for manual ball correction.";
            return false;
        }

        _selectedBallHadPositionOverrideBeforeCorrection = SelectedBall.IsPositionUserOverriden();
        _selectedBallPositionBeforeCorrection = CopyPosition(SelectedBall.GetEffectivePosition());

        IsSelectedBallInPositionCorrectionMode = true;
        SelectedBallPositionCorrectionModeChanged?.Invoke(SelectedSelectable, true);
        SelectedBallChanged?.Invoke(SelectedSelectable);

        statusMessage = "Ball position correction mode started.";
        return true;
    }

    public bool TryMoveSelectedBallToWorldPosition(Vector3 requestedWorldPosition, out string statusMessage)
    {
        if (SelectedBall == null)
        {
            statusMessage = "No ball is currently selected.";
            return false;
        }

        if (!IsSelectedBallInPositionCorrectionMode)
        {
            statusMessage = "Ball position correction mode is not active.";
            return false;
        }

        if (TableService.Instance == null ||
            !TableService.Instance.TryClampBallCenterToPlayableSurface(
                requestedWorldPosition,
                out Vector3 clampedWorldPosition,
                out bool wasClamped))
        {
            statusMessage = "Could not clamp the selected ball to the playable table area.";
            return false;
        }

        SelectedBall.ModifyPosition(new Vector2Float(clampedWorldPosition.x, clampedWorldPosition.z));
        SelectedBallChanged?.Invoke(SelectedSelectable);

        statusMessage = wasClamped
            ? "Ball position corrected and clamped to the playable area."
            : "Ball position corrected.";

        return true;
    }

    public void ConfirmSelectedPositionCorrection()
    {
        if (!IsSelectedBallInPositionCorrectionMode)
            return;

        IsSelectedBallInPositionCorrectionMode = false;
        _selectedBallPositionBeforeCorrection = null;
        _selectedBallHadPositionOverrideBeforeCorrection = false;

        SelectedBallPositionCorrectionModeChanged?.Invoke(SelectedSelectable, false);
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void CancelSelectedPositionCorrection()
    {
        EndSelectedPositionCorrectionInternal(revertToOriginalPosition: true);
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ReleaseSelectedPositionOverride()
    {
        if (SelectedBall == null)
            return;

        SelectedBall.ReleasePositionOverride();
        EndSelectedPositionCorrectionInternal(revertToOriginalPosition: false);
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ReleaseSelectedTypeOverride()
    {
        if (SelectedBall == null)
            return;

        SelectedBall.ReleaseTypeOverride();
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ReleaseSelectedBallIdOverride()
    {
        if (SelectedBall == null)
            return;

        SelectedBall.ReleaseBallIdOverride();
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ResetSelectedOverrides()
    {
        if (SelectedBall == null)
            return;

        EndSelectedPositionCorrectionInternal(revertToOriginalPosition: false);
        SelectedBall.ResetUserOverrides();
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ResetOverridesForSession(TableStateEntry tableStateEntry, BallOverrideSelectable[] activeBallViews = null)
    {
        EndSelectedPositionCorrectionInternal(revertToOriginalPosition: false);
        tableStateEntry?.ResetAllBallOverrides();

        if (activeBallViews != null)
        {
            for (int i = 0; i < activeBallViews.Length; i++)
                activeBallViews[i]?.RefreshVisualState();
        }

        ClearSelection();
    }

    private void EndSelectedPositionCorrectionInternal(bool revertToOriginalPosition)
    {
        if (!IsSelectedBallInPositionCorrectionMode)
            return;

        if (revertToOriginalPosition && SelectedBall != null)
        {
            if (_selectedBallHadPositionOverrideBeforeCorrection && _selectedBallPositionBeforeCorrection != null)
                SelectedBall.ModifyPosition(CopyPosition(_selectedBallPositionBeforeCorrection));
            else
                SelectedBall.ReleasePositionOverride();
        }

        IsSelectedBallInPositionCorrectionMode = false;
        _selectedBallPositionBeforeCorrection = null;
        _selectedBallHadPositionOverrideBeforeCorrection = false;

        SelectedBallPositionCorrectionModeChanged?.Invoke(SelectedSelectable, false);
    }

    private static Vector2Float CopyPosition(Vector2Float source) =>
        source == null
            ? null
            : new Vector2Float(source.X, source.Y);

    private static BallTypeOverrideMenuOption MapBallTypeToMenuOption(BallType ballType) =>
        ballType switch
        {
            BallType.Cue => BallTypeOverrideMenuOption.Cue,
            BallType.Eight => BallTypeOverrideMenuOption.Eightball,
            BallType.Stripe => BallTypeOverrideMenuOption.Striped,
            BallType.Solid => BallTypeOverrideMenuOption.Solid,
            _ => BallTypeOverrideMenuOption.FromPythonStream
        };

    private static BallType MapMenuOptionToBallType(BallTypeOverrideMenuOption option) =>
        option switch
        {
            BallTypeOverrideMenuOption.Cue => BallType.Cue,
            BallTypeOverrideMenuOption.Eightball => BallType.Eight,
            BallTypeOverrideMenuOption.Striped => BallType.Stripe,
            BallTypeOverrideMenuOption.Solid => BallType.Solid,
            _ => BallType.Solid
        };

    private static bool IsSpecialBallType(BallType ballType) =>
        ballType == BallType.Cue || ballType == BallType.Eight;

    private bool HasConflictingSpecialBall(BallType targetType)
    {
        BallOverrideSelectable[] activeSelectables = FindObjectsByType<BallOverrideSelectable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < activeSelectables.Length; i++)
        {
            BallOverrideSelectable selectable = activeSelectables[i];
            if (selectable == null)
                continue;

            Ball candidateBall = selectable.RuntimeBall;
            if (candidateBall == null || candidateBall == SelectedBall)
                continue;

            if (candidateBall.IsIgnoredByUser())
                continue;

            if (candidateBall.BallType == targetType)
                return true;
        }

        return false;
    }

    private bool TryResolveBallIdForTargetType(Ball selectedBall, BallType targetType, out byte resolvedBallId)
    {
        resolvedBallId = targetType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            _ => 0
        };

        if (targetType == BallType.Cue || targetType == BallType.Eight)
            return true;

        int currentBallId = selectedBall != null ? selectedBall.BallId : 0;

        if (IsBallIdInRangeForType(currentBallId, targetType) &&
            !IsBallIdTakenByAnotherBall(selectedBall, targetType, currentBallId))
        {
            resolvedBallId = (byte)currentBallId;
            return true;
        }

        int min = targetType == BallType.Solid ? 1 : 9;
        int max = targetType == BallType.Solid ? 7 : 15;

        for (int ballId = min; ballId <= max; ballId++)
        {
            if (IsBallIdTakenByAnotherBall(selectedBall, targetType, ballId))
                continue;

            resolvedBallId = (byte)ballId;
            return true;
        }

        return false;
    }

    private bool IsBallIdTakenByAnotherBall(Ball selectedBall, BallType targetType, int ballId)
    {
        BallOverrideSelectable[] activeSelectables = FindObjectsByType<BallOverrideSelectable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < activeSelectables.Length; i++)
        {
            BallOverrideSelectable selectable = activeSelectables[i];
            if (selectable == null)
                continue;

            Ball candidateBall = selectable.RuntimeBall;
            if (candidateBall == null || candidateBall == selectedBall)
                continue;

            if (candidateBall.IsIgnoredByUser())
                continue;

            if (candidateBall.BallType == targetType && candidateBall.BallId == ballId)
                return true;
        }

        return false;
    }

    private static bool IsBallIdInRangeForType(int ballId, BallType targetType) =>
        targetType switch
        {
            BallType.Cue => ballId == 0,
            BallType.Eight => ballId == 8,
            BallType.Solid => ballId >= 1 && ballId <= 7,
            BallType.Stripe => ballId >= 9 && ballId <= 15,
            _ => false
        };
}