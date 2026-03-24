// Attach to: Ball placeholder prefab root in PoolSetup scene
using TMPro;
using UnityEngine;

public class BallOverrideSelectable : MonoBehaviour
{
    [Header("Selection Visuals")]
    [SerializeField] private GameObject selectionHighlightRoot; // e.g. ring / outline object
    [SerializeField] private Transform menuAnchor; // e.g. empty child above the ball
    [SerializeField] private TMP_Text debugLabel; // optional

    public Ball RuntimeBall { get; private set; }

    public Transform MenuAnchor => menuAnchor != null ? menuAnchor : transform;

    private void OnEnable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged += HandleSelectedBallChanged;
    }

    private void OnDisable()
    {
        if (ManualBallOverrideService.Instance != null)
            ManualBallOverrideService.Instance.SelectedBallChanged -= HandleSelectedBallChanged;
    }

    public void Bind(Ball runtimeBall)
    {
        RuntimeBall = runtimeBall;
        RefreshVisualState();
    }

    /// <summary>
    /// Wire this to your Meta interaction event on ray select.
    /// </summary>
    public void SelectThisBall()
    {
        if (RuntimeBall == null)
            return;

        ManualBallOverrideService.Instance?.SelectBall(this);
    }

    public void RefreshVisualState()
    {
        bool isSelected =
            ManualBallOverrideService.Instance != null &&
            ManualBallOverrideService.Instance.SelectedSelectable == this;

        if (selectionHighlightRoot != null)
            selectionHighlightRoot.SetActive(isSelected);

        if (debugLabel != null && RuntimeBall != null)
        {
            debugLabel.text =
                $"{RuntimeBall.BallType} #{RuntimeBall.BallId} [{RuntimeBall.UserOverrides}]";
        }
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable _)
    {
        RefreshVisualState();
    }
}