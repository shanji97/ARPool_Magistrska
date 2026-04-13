// Attach to: BallView_new root prefab in the Unity project.
// Visual child: must contain MeshFilter, MeshRenderer, and MeshCollider.

using UnityEngine;

public class BallVisualView : MonoBehaviour
{
    [Header("Renderer")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private MeshFilter targetMeshFilter;
    [SerializeField] private MeshCollider targetMeshCollider;

    [Header("Shared Meshes")]
    [SerializeField] private Mesh _cueMesh;
    [SerializeField] private Mesh _numberedMesh;

    [Header("Shared Materials")]
    [SerializeField] private Material _cueMaterial;
    [SerializeField] private Material _eightballMaterial;
    [SerializeField] private Material _solidMaterial;
    [SerializeField] private Material _stripedMaterial;
    [SerializeField] private Material _ignoredMaterial;

    private Ball _runtimeBall;

    private void Awake()
    {
        AutoResolveReferences();
        RefreshVisualState();
    }

    private void OnValidate()
    {
        AutoResolveReferences();

        if (!Application.isPlaying)
            RefreshVisualState();
    }

    public void Bind(Ball runtimeBall)
    {
        _runtimeBall = runtimeBall;
        RefreshVisualState();
    }

    public void RefreshVisualState()
    {
        if (targetRenderer == null || targetMeshFilter == null)
            return;

        Mesh targetMesh = ResolveMesh(_runtimeBall);
        Material targetMaterial = ResolveMaterial(_runtimeBall);

        if (targetMesh != null && targetMeshFilter.sharedMesh != targetMesh)
            targetMeshFilter.sharedMesh = targetMesh;

        if (targetMeshCollider != null && targetMesh != null && targetMeshCollider.sharedMesh != targetMesh)
            targetMeshCollider.sharedMesh = targetMesh;

        if (targetMaterial != null && targetRenderer.sharedMaterial != targetMaterial)
            targetRenderer.sharedMaterial = targetMaterial;
    }

    private Mesh ResolveMesh(Ball runtimeBall) =>
        runtimeBall?.BallType switch
        {
            BallType.Cue => _cueMesh != null ? _cueMesh : _numberedMesh,
            BallType.Eight => _numberedMesh != null ? _numberedMesh : _cueMesh,
            BallType.Solid => _numberedMesh != null ? _numberedMesh : _cueMesh,
            BallType.Stripe => _numberedMesh != null ? _numberedMesh : _cueMesh,
            _ => _cueMesh != null ? _cueMesh : _numberedMesh
        };

    private Material ResolveMaterial(Ball runtimeBall)
    {
        if (runtimeBall == null)
            return _cueMaterial != null ? _cueMaterial : _solidMaterial;

        if (runtimeBall.IsIgnoredByUser() && _ignoredMaterial != null)
            return _ignoredMaterial;

        return runtimeBall.BallType switch
        {
            BallType.Cue => _cueMaterial,
            BallType.Eight => _eightballMaterial,
            BallType.Solid => _solidMaterial,
            BallType.Stripe => _stripedMaterial,
            _ => _solidMaterial
        };
    }

    private void AutoResolveReferences()
    {
        Transform visualChild = transform.Find("Visual");

        if (targetRenderer == null && visualChild != null)
            targetRenderer = visualChild.GetComponentInChildren<Renderer>(includeInactive: true);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>(includeInactive: true);

        if (targetMeshFilter == null && visualChild != null)
            targetMeshFilter = visualChild.GetComponentInChildren<MeshFilter>(includeInactive: true);

        if (targetMeshFilter == null)
            targetMeshFilter = GetComponentInChildren<MeshFilter>(includeInactive: true);

        if (targetMeshCollider == null && visualChild != null)
            targetMeshCollider = visualChild.GetComponentInChildren<MeshCollider>(includeInactive: true);

        if (targetMeshCollider == null)
            targetMeshCollider = GetComponentInChildren<MeshCollider>(includeInactive: true);
    }
}