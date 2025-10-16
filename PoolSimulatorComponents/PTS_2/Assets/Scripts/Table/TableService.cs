using System.Linq;
using UnityEngine;

public class TableService : MonoBehaviour
{
    public static TableService Instance { get; private set; }

    [Header("Visuals")]
    public GameObject PocketMarkerPrefab;
    public Transform MarkersParent;

    [Tooltip("Offset above table surface to avoid z-fighting")]
    public float SurfaceLift = 0.01f;

    [Tooltip("Scale of default primitive sphere if prefab is null")]
    public float DefaultSphereScale = 0.03f;

    [Header("State (read-only)")]
    public readonly Vector3[] PocketPositions = new Vector3[6];  // TL,TR,ML,MR,BL,BR
    public float TableY = .8f;
    public Vector2 TableSize = new(2.54f, 1.27f);

    [Header("Locked edit behaviour")]
    public bool MaintainRectangleWhenLocked = true;
    private Vector3[] _lastMarkerPosition;

    public bool IsLockedToJitter { get; private set; } = false;
    public bool LockFinalized { get; private set; } = false;
    public float CameraHeightFromFloor { get; private set; }

    public float BallDiameterM = .05715f;
    public const float movingTreshold = .0005f;

    private GameObject[] _markers;

    public void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void LateUpdate()
    {
        if (!LockFinalized || !MaintainRectangleWhenLocked || _markers is null) return;

        // Detect which marker moved the most this frame.
        sbyte moved = -1;
        float maxDelta = 0f;
        for (sbyte i = 0; i < _markers.Length; i++)
        {
            float distance = Vector3.Distance(_markers[i].transform.position, _lastMarkerPosition[i]);
            if (distance > maxDelta)
            {
                maxDelta = distance;
                moved = i;
            }
        }
        if (moved < 0 || maxDelta < movingTreshold)
        {
            for (sbyte i = 0; i < 6; i++)
                _lastMarkerPosition[i] = _markers[i].transform.position;
            return;
        }

        var TL = _markers[0].transform.position;
        var TR = _markers[1].transform.position;
        var ML = _markers[2].transform.position;
        var MR = _markers[3].transform.position;
        var BL = _markers[4].transform.position;
        var BR = _markers[5].transform.position;

        float leftX = 0.5f * (TL.x + BL.x);
        float rightX = 0.5f * (TR.x + BR.x);
        float topZ = 0.5f * (TR.z + MR.z);
        float bottomZ = 0.5f * (BL.z + ML.z);

        // If a specific corner moved, treat its axis as the source of truth:
        switch (moved)
        {
            case 0: // TL moved: update leftX & topZ
                leftX = TL.x; topZ = TL.z; break;
            case 1: // TR moved
                rightX = TR.x; topZ = TR.z; break;
            case 4: // BL moved
                leftX = BL.x; bottomZ = BL.z; break;
            case 5: // BR moved
                rightX = BR.x; bottomZ = BR.z; break;

            case 2: // ML (bottom mid) moved along rail: update bottomZ or centerX
                bottomZ = ML.z; break;
            case 3: // MR (top mid) moved
                topZ = MR.z; break;
        }

        // Reconstruct perfect rectangle:
        float centerX = 0.5f * (leftX + rightX);

        // Corners
        TL = new Vector3(leftX, TableY, topZ);
        TR = new Vector3(rightX, TableY, topZ);
        BL = new Vector3(leftX, TableY, bottomZ);
        BR = new Vector3(rightX, TableY, bottomZ);
        ML = new Vector3(centerX, TableY, bottomZ);
        MR = new Vector3(centerX, TableY, topZ);

        // Apply back
        _markers[0].transform.position = new Vector3(TL.x, TableY + SurfaceLift, TL.z);
        _markers[1].transform.position = new Vector3(TR.x, TableY + SurfaceLift, TR.z);
        _markers[2].transform.position = new Vector3(ML.x, TableY + SurfaceLift, ML.z);
        _markers[3].transform.position = new Vector3(MR.x, TableY + SurfaceLift, MR.z);
        _markers[4].transform.position = new Vector3(BL.x, TableY + SurfaceLift, BL.z);
        _markers[5].transform.position = new Vector3(BR.x, TableY + SurfaceLift, BR.z);
        // Refresh cache
        for (sbyte i = 0; i < 6; i++)
            _lastMarkerPosition[i] = _markers[i].transform.position;
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
                if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
                go.transform.localScale = Vector3.one * DefaultSphereScale;
            }

            if (!go.TryGetComponent<XZOnlyConstraint>(out var constraint))
                constraint = go.AddComponent<XZOnlyConstraint>();
            constraint.Initialize();

            go.name = $"PocketMarker_{i}";
            _markers[i] = go;
        }
        ApplyLockStateToMarkers();

        if (_lastMarkerPosition == null || _lastMarkerPosition.Length != 6)
            _lastMarkerPosition = new Vector3[6];
        for (int i = 0; i < 6; i++) _lastMarkerPosition[i] = _markers[i].transform.position;
    }

    /// <summary>
    /// Sets all six pockets in XZ (meters) and a table Y (meters).
    /// Order: TL, TR, ML, MR, BL, BR. X->Unity X, Z->Unity Z.
    /// Ignored if locked.
    /// </summary>
    public void SetPocketsXZ((float x, float z)[] pocketXZ, float tableY = .8f)
    {
        if (IsLockedToJitter) return;
        if (pocketXZ == null || pocketXZ.Length != 6) return;

        // Cloth plane Y = tableY; ball centers sit +radius above cloth:
        var ballCenterY = tableY + (BallDiameterM * 0.5f);
        TableY = tableY;

        EnsureMarkers();

        for (byte i = 0; i < 6; i++)
        {
            PocketPositions[i] = new Vector3(pocketXZ[i].x, ballCenterY, pocketXZ[i].z);
            _markers[i].transform.position = new Vector3(pocketXZ[i].x, ballCenterY + SurfaceLift, pocketXZ[i].z);
        }
    }

    public void SetPocketsXZ((float x, float y)[] pocketXZ) => SetPocketsXZ(pocketXZ, TableY);

    public void ReapplyPockets(float tableY)
    {
        if (PocketPositions == null || PocketPositions.Length == 0 || _markers == null || _markers.Length != 6)
            return;

        bool wasLocked = IsLockedToJitter;
        if (wasLocked) IsLockedToJitter = false;

        TableY = tableY;
        var ballCenterY = tableY + (BallDiameterM * 0.5f);

        for (int i = 0; i < PocketPositions.Length; i++)
        {
            var p3 = PocketPositions[i];
            p3.y = ballCenterY;
            PocketPositions[i] = p3;

            if (_markers[i] != null)
            {
                var mPos = _markers[i].transform.position;
                mPos.y = ballCenterY + SurfaceLift;
                _markers[i].transform.position = mPos;
            }
        }

        if (wasLocked) IsLockedToJitter = true;
    }

    public void SetFromEnvironmentCache(EnvironmentInfo environmentInfo)
    {
        SetBallDiameter(environmentInfo.PoolTable.BallDiameter_m);
        SetCamera(environmentInfo.CameraCharacteristics.HFromFloor_m);
        SetTable(environmentInfo);
    }

    public void SetBallDiameter(float ballDiameter) => BallDiameterM = ballDiameter > 0f ? ballDiameter : BallDiameterM;

    public void SetTable(float length, float width, float tableY)
    {
        if (IsLockedToJitter) return;

        TableSize = new Vector2(length, width);
        TableY = tableY;
    }

    public void SetTable(EnvironmentInfo env) => SetTable(env.PoolTable.L_m, env.PoolTable.W_m, env.PoolTable.H_m);

    public void SetLocked(bool locked)
    {
        IsLockedToJitter = locked;
        ApplyLockStateToMarkers();
    }

    public void FinalizeLocked()
    {
        SetLocked(true);
        LockFinalized = true;

        _lastMarkerPosition = null;
    }

    private void ApplyLockStateToMarkers()
    {
        if (_markers == null) return;
        foreach (var go in _markers)
        {
            if (go == null) continue;

            if (go.TryGetComponent<XZOnlyConstraint>(out var constraint))
                constraint.GrabbableEnabled = true;
        }
    }

    public void SetCamera(float cameraHeightFromFloor) => CameraHeightFromFloor = cameraHeightFromFloor;
}
