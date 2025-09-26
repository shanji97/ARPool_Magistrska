using UnityEngine;

public class PocketMarkerService : MonoBehaviour
{
    public static PocketMarkerService Instance { get; private set; }

    [Header("Visuals")]
    public GameObject PocketMarkerPrefab;
    public Transform MarkersParent;

    [Tooltip("Offset above table surface to avoid z-fighting")]
    public float SurfaceLift = 0.01f;

    [Tooltip("Scale of default primitive sphere if prefab is null")]
    public float DefaultSphereScale = 0.03f;

    [Header("State (read-only)")]
    public readonly Vector3[] PocketPositions = new Vector3[6];  // TL,TR,ML,MR,BL,BR
    public float TableY = 0.80f;
    public Vector2 TableSize = new(2.54f, 1.27f);

    public bool IsLocked { get; private set; } = false;

    private GameObject[] _markers;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void EnsureMarkers()
    {
        if (_markers?.Length == 6) return;
        _markers = new GameObject[6];

        for (byte i = 0; i < 6; i++)
        {
            GameObject go;
            if (PocketMarkerPrefab != null)
            {
                go = Instantiate(PocketMarkerPrefab, Vector3.zero, Quaternion.identity, MarkersParent);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.SetParent(MarkersParent, worldPositionStays: true);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.localScale = Vector3.one * DefaultSphereScale;
            }

            if (!go.TryGetComponent<XZOnlyConstraint>(out var constraint))
                constraint = go.AddComponent<XZOnlyConstraint>();
            constraint.Initialize();

            go.name = $"PocketMarker_{i}";
            _markers[i] = go;
        }
        ApplyLockStateToMarkers(); // ensure constraint/grab reacts to current IsLocked
    }

    /// <summary>
    /// Sets all six pockets in XZ (meters) and a table Y (meters).
    /// Order: TL, TR, ML, MR, BL, BR. X->Unity X, Z->Unity Z.
    /// Ignored if locked.
    /// </summary>
    public void SetPocketsXZ((float x, float z)[] pocketXZ, float tableY)
    {
        if (IsLocked) return;
        if (pocketXZ == null || pocketXZ.Length != 6) return;

        TableY = tableY;
        EnsureMarkers();

        for (byte i = 0; i < 6; i++)
        {
            PocketPositions[i] = new Vector3(pocketXZ[i].x, TableY, pocketXZ[i].z);
            _markers[i].transform.position = new Vector3(pocketXZ[i].x, TableY + SurfaceLift, pocketXZ[i].z);
        }
    }

    public void SetTable(float length, float width, float tableY)
    {
        if (IsLocked) return;

        TableSize = new Vector2(length, width);
        TableY = tableY;

        if (_markers != null)
        {
            for (byte i = 0; i < 6; i++)
            {
                var p = PocketPositions[i];
                _markers[i].transform.position = new Vector3(p.x, TableY + SurfaceLift, p.z);
            }
        }
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        ApplyLockStateToMarkers();
    }

    private void ApplyLockStateToMarkers()
    {
        if (_markers == null) return;
        foreach (var go in _markers)
        {
            if (go == null) continue;

            // Disable grabbability when locked (so user can’t move them)
            if (go.TryGetComponent<XZOnlyConstraint>(out var constraint))
                constraint.GrabbableEnabled = !IsLocked;

            // If you use XR Grab Interactable or custom grab components,
            // disable them here when locked (example):
            // var grab = go.GetComponent<XRGrabInteractable>(); if (grab) grab.enabled = !IsLocked;
        }
    }
}
