using System;
using UnityEngine;

public class ManualBallOverrideService : MonoBehaviour
{
    public static ManualBallOverrideService Instance { get; private set; }

    public BallOverrideSelectable SelectedSelectable { get; private set; }

    public Ball SelectedBall => SelectedSelectable != null ? SelectedSelectable.RuntimeBall : null;

    public event Action<BallOverrideSelectable> SelectedBallChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
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
        if (SelectedBall == null)
            return;

        SelectedBall.OverrideBallType(newType);
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ApplySelectedBallIdOverride(int newBallId)
    {
        if (SelectedBall == null)
            return;

        SelectedBall.OverrideBallId((byte)Mathf.Clamp(newBallId, 0, 15));
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    public void ResetSelectedOverrides()
    {
        if (SelectedBall == null)
            return;

        SelectedBall.ResetUserOverrides();
        SelectedBallChanged?.Invoke(SelectedSelectable);
    }

    /// <summary>
    /// Call this from your session reset / new-run logic.
    /// </summary>
    public void ResetOverridesForSession(TableStateEntry tableStateEntry, BallOverrideSelectable[] activeBallViews = null)
    {
        tableStateEntry?.ResetAllBallOverrides();

        if (activeBallViews != null)
        {
            for (int i = 0; i < activeBallViews.Length; i++)
            {
                activeBallViews[i]?.RefreshVisualState();
            }
        }

        ClearSelection();
    }
}