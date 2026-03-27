// Attach to: BallView_new prefab root used by TableService in PoolSetup scene
using UnityEngine;

public class BallVisualView : MonoBehaviour
{
    [Header("Renderer")]
    [SerializeField] private Renderer targetRenderer;

    [Header("Shared Materials")]
    [SerializeField] private Material _cueMaterial;
    [SerializeField] private Material _eightballMaterial;
    [SerializeField] private Material _solidMaterial;
    [SerializeField] private Material _stripedMaterial;
    [SerializeField] private Material _ignoredMaterial;

    private Ball _runtimeBall;

    private void Awake() => AutoResolveReferences();

    private void OnValidate() => AutoResolveReferences();

    public void Bind(Ball runtimeBall)
    {
        _runtimeBall = runtimeBall;
        RefreshVisualState();
    }

    public void RefreshVisualState()
    {
        if (_runtimeBall == null || targetRenderer == null)
            return;

        Material targetMaterial = ResolveMaterial(_runtimeBall); // UPDATED: resolve by full runtime state, not only BallType

        if (targetMaterial != null && targetRenderer.sharedMaterial != targetMaterial)
            targetRenderer.sharedMaterial = targetMaterial;
    }

    private Material ResolveMaterial(Ball runtimeBall) => // UPDATED: ignored state now has its own material
        runtimeBall == null
            ? _solidMaterial
            : runtimeBall.IsIgnoredByUser() && _ignoredMaterial != null
                ? _ignoredMaterial
                : runtimeBall.BallType switch
                {
                    BallType.Cue => _cueMaterial,
                    BallType.Eight => _eightballMaterial,
                    BallType.Solid => _solidMaterial,
                    BallType.Stripe => _stripedMaterial,
                    _ => _solidMaterial
                };

    private void AutoResolveReferences()
    {
        if (targetRenderer != null)
            return;

        Transform visualChild = transform.Find("Visual");

        if (visualChild != null)
            targetRenderer = visualChild.GetComponentInChildren<Renderer>(includeInactive: true);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>(includeInactive: true);
    }
}