// Attach to: BallDebugView prefab root
using TMPro;
using UnityEngine;

public class BallDebugView : MonoBehaviour
{
    [Header("View References")]
    [SerializeField] private Renderer visualRenderer;
    [SerializeField] private GameObject selectionHighlightRoot;
    [SerializeField] private Transform menuAnchor;
    [SerializeField] private TMP_Text debugLabel;

    [Header("Fallback Colors")]
    [SerializeField] private Color cueColor = Color.white;
    [SerializeField] private Color eightColor = Color.black;
    [SerializeField] private Color solidColor = new(0.95f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color stripeColor = new(0.2f, 0.45f, 1f, 1f);
    [SerializeField] private Color unknownColor = Color.gray;

    private BallOverrideSelectable _selectable;

    private void Awake()
    {
        _selectable = GetComponent<BallOverrideSelectable>();

        if (_selectable == null)
            _selectable = gameObject.AddComponent<BallOverrideSelectable>();

        // Keep the selectable script wired from prefab references.
        // The actual runtime Ball instance is bound later by TableService.
    }

    public void ConfigureSelectableReferences()
    {
        if (_selectable == null)
            _selectable = GetComponent<BallOverrideSelectable>();

        if (_selectable == null)
            return;

#if UNITY_EDITOR
        // This method is mainly here so you can validate the prefab quickly in the editor.
#endif
    }

    public void ApplyVisualState(Ball runtimeBall)
    {
        if (runtimeBall == null)
            return;

        ApplyColor(runtimeBall.BallType);
        ApplyDebugLabel(runtimeBall);
        ApplyHighlightState();
    }

    public void ApplyHighlightState()
    {
        if (_selectable == null)
            _selectable = GetComponent<BallOverrideSelectable>();

        if (_selectable == null || selectionHighlightRoot == null)
            return;

        bool isSelected =
            ManualBallOverrideService.Instance != null &&
            ManualBallOverrideService.Instance.SelectedSelectable == _selectable;

        selectionHighlightRoot.SetActive(isSelected);
    }

    private void ApplyColor(BallType ballType)
    {
        if (visualRenderer == null)
            return;

        Material material = visualRenderer.material;
        material.color = ballType switch
        {
            BallType.Cue => cueColor,
            BallType.Eight => eightColor,
            BallType.Solid => solidColor,
            BallType.Stripe => stripeColor,
            _ => unknownColor
        };
    }

    private void ApplyDebugLabel(Ball runtimeBall)
    {
        if (debugLabel == null || runtimeBall == null)
            return;

        debugLabel.text = $"{runtimeBall.BallType} #{runtimeBall.BallId}\n[{runtimeBall.UserOverrides}]";
    }

    public Renderer GetVisualRenderer() => visualRenderer;

    public GameObject GetSelectionHighlightRoot() => selectionHighlightRoot;

    public Transform GetMenuAnchor() => menuAnchor;

    public TMP_Text GetDebugLabel() => debugLabel;
}