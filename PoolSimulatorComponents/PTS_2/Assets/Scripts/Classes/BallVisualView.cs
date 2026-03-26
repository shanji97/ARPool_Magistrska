using UnityEngine;

public class BallVisualView : MonoBehaviour
{
    [Header("Renderer")]
    [SerializeField] private Renderer targetRenderer;

    [Header("Shared Materials")]
    [SerializeField] private Material cueMaterial;
    [SerializeField] private Material eightballMaterial;
    [SerializeField] private Material solidMaterial;
    [SerializeField] private Material stripedMaterial;

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

        Material targetMaterial = ResolveMaterial(_runtimeBall.BallType);

        if (targetMaterial != null && targetRenderer.sharedMaterial != targetMaterial)
            targetRenderer.sharedMaterial = targetMaterial;
    }

    private Material ResolveMaterial(BallType ballType) =>
        ballType switch
        {
            BallType.Cue => cueMaterial,
            BallType.Eight => eightballMaterial,
            BallType.Solid => solidMaterial,
            BallType.Stripe => stripedMaterial,
            _ => solidMaterial
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
