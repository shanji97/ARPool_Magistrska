using System;
using UnityEngine;

// Attach to: GameplaySystems/ManualBallOverrideService in PoolSetup scene
// Branch: ISSUE-84
// Issue: #84 manual ball override selection state + type dropdown validation
public class ManualBallOverrideService : MonoBehaviour
{
    public static ManualBallOverrideService Instance { get; private set; }

    public BallOverrideSelectable SelectedSelectable { get; private set; }

    public Ball SelectedBall => SelectedSelectable != null ? SelectedSelectable.RuntimeBall : null;

    public bool HasSelection => SelectedBall != null;

    public event Action<BallOverrideSelectable> SelectedBallChanged;

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

        SelectedSelectable = selectable;
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ClearSelection()
    {
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
        SelectedBall.AssignBallId(resolvedBallId); // Changed for ISSUE-84.1: keep the resolved valid number without forcing a separate number-override flag.

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

        SelectedBall.ResetUserOverrides();
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ResetOverridesForSession(TableStateEntry tableStateEntry, BallOverrideSelectable[] activeBallViews = null)
    {
        tableStateEntry?.ResetAllBallOverrides();

        if (activeBallViews != null)
        {
            for (int i = 0; i < activeBallViews.Length; i++)
                activeBallViews[i]?.RefreshVisualState();
        }

        ClearSelection();
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