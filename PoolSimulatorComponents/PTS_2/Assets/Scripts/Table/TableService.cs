using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TableService : MonoBehaviour
{
    public static TableService Instance { get; private set; }

    [Header("Visuals")]
    public GameObject PocketMarkerPrefab;
    public Transform MarkersParent;
    public byte PocketCount { get; private set; } = 0;
    public byte DiamondCount { get; private set; } = 0;
    public bool HasAllPockets() => PocketCount == MAX_POCKET_COUNT;

    [Header("Ball Visualization")]
    public GameObject BallViewPrefab;
    public Transform BallViewsParent;
    public bool EnableBallVisualization = true;
    public bool RequireQrAlignmentBeforeBallVisualization = true;
    public bool DebugBypassQrAlignmentGate = false;
    public float BallViewSurfaceLiftM = 0.002f;
    public float BallReuseMatchRadiusM = 0.09f;
    public bool VerboseBallLogs = true;

    public bool QrAlignmentConfirmedForBallVisualization { get; private set; } = false;

    private readonly List<IncomingDetectedBallRecord> _latestDetectedBallSnapshot = new();
    private readonly Dictionary<Ball, GameObject> _ballViewsByBall = new();
    private TableStateEntry _activeTableStateEntry;
    private int _ballViewSequence = 0;

    [Header("Diamond Debug Preview")]
    public bool ShowComputedDiamondPreview = true;
    public bool ShowDiamondEditorGizmos = true;
    public bool ShowDiamondRuntimeMarkers = true;
    public bool HideDiamondPreviewAfterFinalize = true;
    public bool ShowDiamondIndices = true;

    [Tooltip("Optional prefab for runtime diamond preview markers. Falls back to primitive spheres if null.")]
    public GameObject DiamondMarkerPrefab;

    [Tooltip("Optional parent for runtime diamond markers. Falls back to MarkersParent, then this transform.")]
    public Transform DiamondMarkersParent;

    [Tooltip("Extra height above the pocket marker plane to avoid z-fighting.")]
    public float DiamondSurfaceLift = 0.015f;

    [Tooltip("Radius of editor gizmo spheres.")]
    public float DiamondGizmoRadius = 0.015f;

    [Tooltip("Scale of fallback runtime spheres when DiamondMarkerPrefab is null.")]
    public float DiamondRuntimeScale = 0.02f;

    public Color DiamondGizmoColor = Color.yellow;
    public Color DiamondRuntimeColor = Color.yellow;

    private readonly List<GameObject> _runtimeDiamondMarkers = new();
    private List<DiamondMarkerData> _computedDiamondMarkerData = new();

    [Header("Diamond Rail Anchor Calibration")]
    public float DiamondLongRailCornerInsetM = 0.09f; // MODIFIED: long rail corner-pocket inset
    public float DiamondLongRailSidePocketInsetM = 0.07f;
    public float DiamondShortRailCornerInsetM = 0.06f; // MODIFIED: short rail corner-pocket inset 


    [Tooltip("Offset above table surface to avoid z-fighting")]
    public float SurfaceLift = 0.01f;

    [Tooltip("Scale of default primitive sphere if prefab is null")]
    public float DefaultSphereScale = 0.03f;

    [Header("State (read-only)")]
    public Vector3[] PocketPositions { get; private set; }  // TL,TR,ML,MR,BL,BR

    private List<DiamondMarkerData> _diamondMarkerData = new();

    public float TableY { get; private set; } = -1f;
    public bool IsTableHeightSet() => TableY > 0;

    public Vector2 TableSize = new(-1, -1);
    public bool Is2DTableSet() => TableSize.x > -1 && TableSize.y > -1;

    [Header("Locked edit behaviour")]
    public bool MaintainRectangleWhenLocked = true;
    [Tooltip("Preserve the current table rotation while the user manually drags pocket markers after QR alignment.")]
    public bool MaintainPocketOrientationDuringEditing = true;
    private Vector3[] _lastMarkerPosition;
    private bool _externalPocketUpdatesSuppressed;
    private bool _hasPocketEditingBasis;
    private Vector3 _pocketEditingBasisOrigin;
    private Vector3 _pocketEditingLongAxis = Vector3.right;
    private Vector3 _pocketEditingShortAxis = Vector3.forward;


    [Header("Near-Pocket Suppression")]
    public bool EnableNearPocketSuppression = true;
    public bool TreatNearPocketZoneAsPocketed = true;
    public bool ShowNearPocketRuntimeMarkers = true;
    public bool VerboseNearPocketLogs = true;

    [Tooltip("Ball center distance to nearest pocket center below which the ball is treated as pocketed/suppressed.")]
    public float PocketCaptureThresholdM = 0.09f;

    [Tooltip("Ball center distance to nearest pocket center below which the ball is treated as near-pocket / ambiguous.")]
    public float PocketAmbiguousThresholdM = 0.13f;

    [Tooltip("Extra distance required before a previously suppressed ball becomes visible again. Prevents flicker/toggling.")]
    public float PocketSuppressionReleaseMarginM = 0.02f;

    [Tooltip("2D distance used to match current detections against existing near-pocket memory entries.")]
    public float PocketSuppressionMatchRadiusM = 0.08f;

    [Tooltip("How long transient near-pocket suppression memory is retained for non-special balls.")]
    public float PocketSuppressionMemorySeconds = 0.75f;

    [Tooltip("Extra height above the table plane for near-pocket debug markers.")]
    public float NearPocketDebugLift = 0.03f;

    [Tooltip("Scale of fallback near-pocket runtime spheres when NearPocketDebugMarkerPrefab is null.")]
    public float NearPocketDebugMarkerScale = 0.028f;

    [Tooltip("Optional prefab for near-pocket runtime debug markers. Falls back to primitive spheres if null.")]
    public GameObject NearPocketDebugMarkerPrefab;

    [Tooltip("Optional parent for near-pocket runtime debug markers. Falls back to MarkersParent, then this transform.")]
    public Transform NearPocketDebugMarkersParent;

    public Color NearPocketAmbiguousColor = new(1f, 0.65f, 0f, 1f);
    public Color NearPocketPocketedColor = Color.red;
    public Color NearPocketSpecialColor = Color.magenta;

    private readonly List<NearPocketBallMemory> _nearPocketBallMemory = new();
    private readonly List<GameObject> _runtimeNearPocketMarkers = new();

    // multiple booleans -> to byte 
    public bool CueBallPocketed { get; private set; } = false;
    public bool EightBallPocketed { get; private set; } = false;
    public byte CueBallPocketIndex { get; private set; } = byte.MaxValue;
    public byte EightBallPocketIndex { get; private set; } = byte.MaxValue;


    // Two bools for the price of one
    public bool IsLockedToJitter { get; private set; } = false; // ->mo to bitwise operations with lockfinalize (
    public bool LockFinalized { get; private set; } = false; // -> move to bitwise operations with is locked to jitter

    public float CameraHeightFromFloor { get; private set; } = -1f;
    public bool IsCameraFromFloorSet() => CameraHeightFromFloor > 0;

    public float BallDiameterM = -1f;
    public bool IsBallDiameterSet() => BallDiameterM > 0;

    public float BallCircumferenceM = -1f;
    public bool IsBallCircumferenceSet() => BallCircumferenceM > 0;

    public bool AreBallPropertiesSet() => IsBallDiameterSet() && IsBallCircumferenceSet();

    // Full environment readiness.
    // Keep this for compatibility with older code that still expects the old meaning.
    public bool ArePropertiesParsed() => Is2DTableSet() && IsTableHeightSet() && IsCameraFromFloorSet();

    public bool CanApplyQrAlignedPocketLayout() => IsTableHeightSet();

    public bool AreDiamondsParsed() => DiamondCount == MAX_DIAMOND_COUNT;

    public bool IsLoadedFromBackup { get; private set; } = false;
    public void SetEnvironmentLoadedFromBackup(bool isLoadedFromBackup) => IsLoadedFromBackup = isLoadedFromBackup;

    private bool VerboseDiamondLogs()
    {
#if UNITY_EDITOR
        return true;
#else
            return false;
#endif
    }

    private bool _enviromentSaved = false;

    [Header("QR Alignment")]
    public string[] QR_CODE_MARKER_VALUES =
{
    "ARPOOL_MARKER_01",
    "ARPOOL_MARKER_02",
    "ARPOOL_MARKER_03",
    "ARPOOL_MARKER_04",
    "ARPOOL_MARKER_05",
    "ARPOOL_MARKER_06"
};

    [Range(4, 6)]
    public byte QR_MARKER_COUNT = 6;

    [Min(0.05f)]
    public float QR_CODE_WHOLE_PAPER_SIZE_M = 0.16f;

    [Tooltip("If enabled, validation uses the direct center-to-center spans below instead of gap + paper size.")]
    public bool QR_USE_DIRECT_CENTER_SPAN_VALUES = true;

    [Tooltip("Direct measured center-to-center distance between the top and bottom corner QR markers.")]
    [Min(0f)]
    public float QR_CORNER_CENTER_TOP_BOTTOM_M = 0.445f;

    [Tooltip("Direct measured center-to-center distance between the left and right corner QR markers.")]
    [Min(0f)]
    public float QR_CORNER_CENTER_LEFT_RIGHT_M = 1.65f;

    [Tooltip("Measured clear gap between the top and bottom corner QR papers, edge-to-edge, in meters.")]
    [Range(0f, 2f)]
    public float QR_CORNER_PAPER_GAP_TOP_BOTTOM_M = 0.445f;

    [Tooltip("Measured clear gap between the left and right corner QR papers, edge-to-edge, in meters.")]
    [Range(0f, 3f)]
    public float QR_CORNER_PAPER_GAP_LEFT_RIGHT_M = 1.65f;

    [Tooltip("Optional correction added after gap + paper size for the top/bottom QR center-span validation.")]
    public float QR_CORNER_CENTER_EXTRA_TOP_BOTTOM_M = 0f;

    [Tooltip("Optional correction added after gap + paper size for the left/right QR center-span validation.")]
    public float QR_CORNER_CENTER_EXTRA_LEFT_RIGHT_M = 0f;

    [Tooltip("Allowed deviation for QR center-span validation.")]
    [Min(0f)]
    public float QR_CORNER_SPAN_TOLERANCE_M = 0.10f;

    [Tooltip("If true, marker 05 and 06 refine the top and bottom rail placement when present.")]
    public bool QR_USE_MIDDLE_MARKERS_WHEN_PRESENT = true;

    [Tooltip("Distance from a corner QR marker center to the corresponding corner pocket center, measured inward toward the table center.")]
    [Min(0f)]
    public float QR_CORNER_MARKER_TO_CORNER_POCKET_OFFSET_M = 0.14f;

    [Tooltip("Distance from marker 05 or 06 to the corresponding top or bottom rail estimate, measured inward toward the table center.")]
    [Min(0f)]
    public float QR_MIDDLE_MARKER_TO_RAIL_OFFSET_M = 0.14f;

    [Header("Raw pocket detection state")]
    public Vector2[] RawDetectedPocketXZ { get; private set; }
    public bool HasRawDetectedPocketData { get; private set; }

    [Tooltip("Pocket handle lift above the table surface.")]
    [Min(0f)]
    public float PocketMarkerLiftAboveTableM = 0.01f;
    private const float movingTreshold = .0005f;

    private GameObject[] _markers;
    public readonly byte MAX_POCKET_COUNT = 6;
    public readonly byte MAX_DIAMOND_COUNT = 18;

    public readonly byte MAX_BALL_COUNT = 16;

    // 0 - 6 solids, 7 eight, 8 - 14 stripes, cue 15
    private List<Vector3Float> _balls = null;

    private List<TableStateEntry> _tableStateEntries = null;

    private static readonly HashSet<string> MarkerInteractionComponentTypeNames = new()
    {
        "Grabbable",
        "HandGrabInteractable",
        "DistanceHandGrabInteractable",
        "RayInteractable",
        "PokeInteractable"
    };

    // UPDATED: one-time warning guard
    private bool _warnedMissingBallSpec = false;



    public void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        PocketPositions = new Vector3[MAX_POCKET_COUNT];
        _tableStateEntries = new();

        EnsureBallBufferSize();

        RawDetectedPocketXZ = new Vector2[MAX_POCKET_COUNT];
    }

    public void LateUpdate()
    {
        if (!LockFinalized && MaintainRectangleWhenLocked && _markers is not null)
            HandlePocketMarkers();

        RefreshComputedDiamondPreview();
        CleanupExpiredNearPocketBallMemory();
        RefreshNearPocketDebugMarkers();
        TryApplyCachedDetectedBallSnapshot();
    }

    private void Cleanup()
    {
        DestroyRuntimeDiamondMarkers();
        DestroyNearPocketDebugMarkers();
        DestroyBallViews();
    }

    public void OnDestroy()
    {
        if (_tableStateEntries.Any())
            AppSettings.Instance.AddTableStateEntries(_tableStateEntries);
        Cleanup();
    }

    public void OnApplicationQuit()
    {
        if (_tableStateEntries.Any())
            AppSettings.Instance.AddTableStateEntries(_tableStateEntries);

        Cleanup();
    }

    private void OnDrawGizmos()
    {
        if (!ShowDiamondEditorGizmos)
            return;

        if (_computedDiamondMarkerData == null || _computedDiamondMarkerData.Count != MAX_DIAMOND_COUNT)
            return;

        Color previousColor = Gizmos.color;
        Gizmos.color = DiamondGizmoColor;

        float y = Application.isPlaying && TableY > 0f
            ? GetDiamondPreviewY()
            : transform.position.y;

        for (int i = 0; i < _computedDiamondMarkerData.Count; i++)
        {
            DiamondMarkerData d = _computedDiamondMarkerData[i];
            Vector3 world = new(d.XZ.X, y, d.XZ.Y);

            Gizmos.DrawSphere(world, DiamondGizmoRadius);

#if UNITY_EDITOR
            if (ShowDiamondIndices)
            {
                UnityEditor.Handles.color = DiamondGizmoColor;
                UnityEditor.Handles.Label(world + Vector3.up * 0.02f, d.Index.ToString());
            }
#endif
        }

        Gizmos.color = previousColor;
    }

    public void SetRawDetectedPocketXZ((float x, float z)[] pocketXZ)
    {
        if (pocketXZ == null || pocketXZ.Length != MAX_POCKET_COUNT)
        {
            HasRawDetectedPocketData = false;
            return;
        }

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
            RawDetectedPocketXZ[i] = new Vector2(pocketXZ[i].x, pocketXZ[i].z);

        HasRawDetectedPocketData = true;
    }

    public void IncrementSuccessfullyParsedPocketCount()
    {
        if (PocketCount == MAX_POCKET_COUNT) return;
        PocketCount++;
    }

    public void IncrementSuccessfullyParsedDiamondEdgeCount()
    {
        if (DiamondCount == MAX_DIAMOND_COUNT) return;
        DiamondCount++;
    }

    private void PrivateSetEdgeDiamonds((float x, float z, byte i, float c)[] diamonds, float tableY)
    {
        if (IsLockedToJitter) return;
        if (diamonds?.Length != MAX_DIAMOND_COUNT) return;

        _diamondMarkerData = ProcessDiamonds(diamonds);
    }
    private byte CopyRailWithAssignedIndices(List<(float x, float z, byte i, float c)> source, (float x, float z, byte i, float c)[] target, byte startIndex)
    {
        for (int i = 0; i < source.Count; i++)
        {
            byte assignedIndex = (byte)(startIndex + i);
            var entry = source[i];

            // MODIFIED: the parsed incoming index is intentionally ignored.
            target[startIndex + i] = (entry.x, entry.z, assignedIndex, entry.c);
        }

        return (byte)(startIndex + source.Count);
    }

    private bool CanPreviewComputedDiamonds()
    {
        // ADDED: ISSUE-82 preview only requires a valid table height and all six pockets.
        return ShowComputedDiamondPreview
            && IsTableHeightSet()
            && PocketPositions != null
            && PocketPositions.Length == MAX_POCKET_COUNT
            && HasAllPockets();
    }

    private float GetDiamondPreviewY() => GetPocketMarkerY(TableY) + DiamondSurfaceLift;

    private static void AddComputedRailDiamonds(
        List<DiamondMarkerData> target,
        Vector3 start,
        Vector3 end,
        byte diamondCount,
        ref byte nextIndex)
    {
        // ADDED: evenly spaced diamonds along one rail segment.
        int divisor = diamondCount + 1;

        for (int step = 1; step <= diamondCount; step++)
        {
            float t = (float)step / divisor;
            Vector3 p = Vector3.Lerp(start, end, t);

            target.Add(new DiamondMarkerData()
            {
                XZ = new Vector2Float(p.x, p.z),
                Index = nextIndex++,
                Confidence = 1f // ADDED: computed preview, not detector confidence
            });
        }
    }

    private List<DiamondMarkerData> BuildComputedDiamondLayoutFromPockets()
    {
        if (!CanPreviewComputedDiamonds())
            return null;

        // Pocket order in this project:
        // 0 TL, 1 TR, 2 ML (bottom middle), 3 MR (top middle), 4 BL, 5 BR
        Vector3 tl = PocketPositions[0];
        Vector3 tr = PocketPositions[1];
        Vector3 ml = PocketPositions[2];
        Vector3 mr = PocketPositions[3];
        Vector3 bl = PocketPositions[4];
        Vector3 br = PocketPositions[5];

        List<DiamondMarkerData> computed = new(MAX_DIAMOND_COUNT);
        byte index = 0;

        // MODIFIED: split each long rail into TWO independent 3-diamond segments.
        // Bottom rail canonical order: left -> right
        (Vector3 bottomLeftStart, Vector3 bottomLeftEnd) = BuildInsetRailSegment(
            bl,
            ml,
            DiamondLongRailCornerInsetM,
            DiamondLongRailSidePocketInsetM);

        (Vector3 bottomRightStart, Vector3 bottomRightEnd) = BuildInsetRailSegment(
            ml,
            br,
            DiamondLongRailSidePocketInsetM,
            DiamondLongRailCornerInsetM);

        // Right short rail canonical order: bottom -> top
        (Vector3 rightStart, Vector3 rightEnd) = BuildInsetRailSegment(
            br,
            tr,
            DiamondShortRailCornerInsetM,
            DiamondShortRailCornerInsetM);

        // Top rail canonical order: right -> left
        (Vector3 topRightStart, Vector3 topRightEnd) = BuildInsetRailSegment(
            tr,
            mr,
            DiamondLongRailCornerInsetM,
            DiamondLongRailSidePocketInsetM);

        (Vector3 topLeftStart, Vector3 topLeftEnd) = BuildInsetRailSegment(
            mr,
            tl,
            DiamondLongRailSidePocketInsetM,
            DiamondLongRailCornerInsetM);

        // Left short rail canonical order: top -> bottom
        (Vector3 leftStart, Vector3 leftEnd) = BuildInsetRailSegment(
            tl,
            bl,
            DiamondShortRailCornerInsetM,
            DiamondShortRailCornerInsetM);

        // MODIFIED: 6 diamonds on each long rail are now 3 + 3, split by the side pocket.
        AddComputedRailDiamonds(computed, bottomLeftStart, bottomLeftEnd, 3, ref index);
        AddComputedRailDiamonds(computed, bottomRightStart, bottomRightEnd, 3, ref index);
        AddComputedRailDiamonds(computed, rightStart, rightEnd, 3, ref index);
        AddComputedRailDiamonds(computed, topRightStart, topRightEnd, 3, ref index);
        AddComputedRailDiamonds(computed, topLeftStart, topLeftEnd, 3, ref index);
        AddComputedRailDiamonds(computed, leftStart, leftEnd, 3, ref index);

        if (computed.Count != MAX_DIAMOND_COUNT)
        {
            Debug.LogWarning(
                $"[TableService] Computed diamond preview count mismatch. " +
                $"Expected {MAX_DIAMOND_COUNT}, got {computed.Count}.");
            return null;
        }

        if (VerboseDiamondLogs())
        {
            Debug.Log(
                "[TableService] Computed diamond preview updated from segmented rail anchors. " +
                $"LongCornerInset={DiamondLongRailCornerInsetM:F3}m, " +
                $"LongSideInset={DiamondLongRailSidePocketInsetM:F3}m, " +
                $"ShortCornerInset={DiamondShortRailCornerInsetM:F3}m");
        }

        return computed;
    }

    private void RefreshComputedDiamondPreview()
    {
        // ADDED: hide preview after final confirmation unless explicitly kept.
        if (!ShowComputedDiamondPreview || (LockFinalized && HideDiamondPreviewAfterFinalize))
        {
            _computedDiamondMarkerData = null;
            SetRuntimeDiamondMarkersActive(false);
            return;
        }

        _computedDiamondMarkerData = BuildComputedDiamondLayoutFromPockets();

        if (_computedDiamondMarkerData == null || _computedDiamondMarkerData.Count != MAX_DIAMOND_COUNT)
        {
            SetRuntimeDiamondMarkersActive(false);
            return;
        }

        if (!ShowDiamondRuntimeMarkers)
        {
            SetRuntimeDiamondMarkersActive(false);
            return;
        }

        EnsureRuntimeDiamondMarkers(_computedDiamondMarkerData.Count);

        float y = GetDiamondPreviewY();

        for (int i = 0; i < _computedDiamondMarkerData.Count; i++)
        {
            DiamondMarkerData d = _computedDiamondMarkerData[i];
            GameObject marker = _runtimeDiamondMarkers[i];

            if (marker == null)
                continue;

            marker.transform.SetPositionAndRotation(
                new Vector3(d.XZ.X, y, d.XZ.Y),
                Quaternion.identity
            );

            if (!marker.activeSelf)
                marker.SetActive(true);
        }

        SetRuntimeDiamondMarkersActive(true);
    }

    private static (Vector3 start, Vector3 end) BuildInsetRailSegment(
    Vector3 start,
    Vector3 end,
    float startInsetM,
    float endInsetM)
    {
        // MODIFIED: supports different inset values on each side of the rail segment.
        Vector3 delta = end - start;
        float length = delta.magnitude;

        if (length <= Mathf.Epsilon)
            return (start, end);

        float clampedStartInset = Mathf.Clamp(startInsetM, 0f, length * 0.45f);
        float clampedEndInset = Mathf.Clamp(endInsetM, 0f, length * 0.45f);

        // MODIFIED: prevent overlap if both insets become too large.
        float totalInset = clampedStartInset + clampedEndInset;
        if (totalInset >= length)
        {
            float safeScale = (length * 0.9f) / totalInset;
            clampedStartInset *= safeScale;
            clampedEndInset *= safeScale;
        }

        Vector3 direction = delta / length;

        return
        (
            start + direction * clampedStartInset,
            end - direction * clampedEndInset
        );
    }

    private void EnsureRuntimeDiamondMarkers(int requiredCount)
    {
        while (_runtimeDiamondMarkers.Count < requiredCount)
        {
            _runtimeDiamondMarkers.Add(CreateRuntimeDiamondMarker(_runtimeDiamondMarkers.Count));
        }

        for (int i = 0; i < _runtimeDiamondMarkers.Count; i++)
        {
            _runtimeDiamondMarkers[i]?.SetActive(i < requiredCount);
        }
    }

    private GameObject CreateRuntimeDiamondMarker(int index)
    {
        Transform parent = DiamondMarkersParent != null ? DiamondMarkersParent : (MarkersParent != null ? MarkersParent : transform);

        GameObject go;

        if (DiamondMarkerPrefab != null)
        {
            go = Instantiate(DiamondMarkerPrefab, Vector3.zero, Quaternion.identity, parent);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            if (parent != null)
                go.transform.SetParent(parent, worldPositionStays: true);

            // ADDED: fallback runtime sphere scale.
            go.transform.localScale = Vector3.one * DiamondRuntimeScale;
        }

        go.name = $"DiamondPreview_{index}";

        // ADDED: these are debug visuals only, so they must not interfere with interaction/physics.
        Collider[] colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }

        Rigidbody[] rigidbodies = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        foreach (var rb in rigidbodies)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        ApplyDiamondRuntimeColor(go, DiamondRuntimeColor);
        go.SetActive(false);

        return go;
    }

    private void ApplyDiamondRuntimeColor(GameObject target, Color color)
    {
        if (target == null)
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            // ADDED: use instantiated runtime material for debug markers.
            Material material = renderer.material;
            material.color = color;
        }
    }

    private void SetRuntimeDiamondMarkersActive(bool active)
    {
        for (int i = 0; i < _runtimeDiamondMarkers.Count; i++)
        {
            if (_runtimeDiamondMarkers[i] != null && _runtimeDiamondMarkers[i].activeSelf != active)
                _runtimeDiamondMarkers[i].SetActive(active);
        }
    }

    private void DestroyRuntimeDiamondMarkers()
    {
        for (int i = _runtimeDiamondMarkers.Count - 1; i >= 0; i--)
        {
            GameObject marker = _runtimeDiamondMarkers[i];
            if (marker == null)
                continue;

            if (Application.isPlaying)
                Destroy(marker);
            else
                DestroyImmediate(marker);
        }

        _runtimeDiamondMarkers.Clear();
    }

    private List<DiamondMarkerData> ProcessDiamonds(List<DiamondMarkerData> diamondMarkerData)
    {
        (float x, float z, byte i, float c)[] diamonds = diamondMarkerData.Select(dmd => (
            x: dmd.XZ.X,
            z: dmd.XZ.Y,
            i: dmd.Index,
            c: dmd.Confidence
        )).ToArray();

        return ProcessDiamonds(diamonds);
    }

    private List<DiamondMarkerData> ProcessDiamonds((float x, float z, byte i, float c)[] diamonds)
    {
        if (diamonds == null || diamonds.Length != MAX_DIAMOND_COUNT)
        {
            Debug.LogWarning("[TableService] ProcessDiamonds received invalid input.");
            return null;
        }

        if (PocketPositions == null || PocketPositions.Length != MAX_POCKET_COUNT)
        {
            Debug.LogWarning("[TableService] ProcessDiamonds requires six pocket positions.");
            return null;
        }

        // Pocket order in this project:
        // 0 TL, 1 TR, 2 ML, 3 MR, 4 BL, 5 BR
        Vector3 tl = PocketPositions[0];
        Vector3 tr = PocketPositions[1];
        Vector3 ml = PocketPositions[2];
        Vector3 mr = PocketPositions[3];
        Vector3 bl = PocketPositions[4];
        Vector3 br = PocketPositions[5];

        float leftX = 0.5f * (tl.x + bl.x);
        float rightX = 0.5f * (tr.x + br.x);
        float topZ = 0.5f * (tl.z + tr.z);
        float bottomZ = 0.5f * (bl.z + br.z);

        var bottomRail = new List<(float x, float z, byte i, float c)>(6);
        var rightRail = new List<(float x, float z, byte i, float c)>(3);
        var topRail = new List<(float x, float z, byte i, float c)>(6);
        var leftRail = new List<(float x, float z, byte i, float c)>(3);

        for (int d = 0; d < diamonds.Length; d++)
        {
            var current = diamonds[d];

            float distanceToLeft = Mathf.Abs(current.x - leftX);
            float distanceToRight = Mathf.Abs(current.x - rightX);
            float distanceToTop = Mathf.Abs(current.z - topZ);
            float distanceToBottom = Mathf.Abs(current.z - bottomZ);

            float minDistance = distanceToLeft;
            TableRail closestRail = TableRail.Left;

            if (distanceToRight < minDistance)
            {
                minDistance = distanceToRight;
                closestRail = TableRail.Right;
            }

            if (distanceToTop < minDistance)
            {
                minDistance = distanceToTop;
                closestRail = TableRail.Top;
            }

            if (distanceToBottom < minDistance)
            {
                minDistance = distanceToBottom;
                closestRail = TableRail.Bottom;
            }

            switch (closestRail)
            {
                case TableRail.Bottom:
                    bottomRail.Add(current);
                    break;

                case TableRail.Right:
                    rightRail.Add(current);
                    break;

                case TableRail.Top:
                    topRail.Add(current);
                    break;

                case TableRail.Left:
                    leftRail.Add(current);
                    break;
            }
        }

        // MODIFIED: deterministic canonical clockwise ordering.
        bottomRail = bottomRail.OrderBy(d => d.x).ToList();      // left -> right
        rightRail = rightRail.OrderBy(d => d.z).ToList();        // bottom -> top
        topRail = topRail.OrderByDescending(d => d.x).ToList();  // right -> left
        leftRail = leftRail.OrderByDescending(d => d.z).ToList(); // top -> bottom

        if (bottomRail.Count != 6 || rightRail.Count != 3 || topRail.Count != 6 || leftRail.Count != 3)
        {
            Debug.LogWarning(
                "[TableService] Unexpected diamond rail split. " +
                $"Bottom={bottomRail.Count}, Right={rightRail.Count}, Top={topRail.Count}, Left={leftRail.Count}. " +
                "Using fallback global ordering."
            );

            // Fallback: preserve deterministic output even if the rail classification is imperfect.
            var fallback = diamonds
                .OrderBy(d => d.z)
                .ThenBy(d => d.x)
                .ToArray();

            for (byte i = 0; i < fallback.Length; i++)
                fallback[i] = (fallback[i].x, fallback[i].z, i, fallback[i].c);

            List<DiamondMarkerData> fallBackData = new();

            for (byte i = 0; i < fallback.Length; i++)
            {
                fallBackData.Add(
                    new DiamondMarkerData()
                    {
                        XZ = new Vector2Float(fallback[i].x, fallback[i].z),
                        Index = fallback[i].i,
                        Confidence = fallback[i].c,
                    });
            }

            return fallBackData;
        }

        var ordered = new (float x, float z, byte i, float c)[MAX_DIAMOND_COUNT];
        byte index = 0;

        index = CopyRailWithAssignedIndices(bottomRail, ordered, index);
        index = CopyRailWithAssignedIndices(rightRail, ordered, index);
        index = CopyRailWithAssignedIndices(topRail, ordered, index);
        index = CopyRailWithAssignedIndices(leftRail, ordered, index);

        List<DiamondMarkerData> orderedData = new();

        for (byte i = 0; i < ordered.Length; i++)
        {
            orderedData.Add(
                new DiamondMarkerData()
                {
                    XZ = new Vector2Float(ordered[i].x, ordered[i].z),
                    Index = ordered[i].i,
                    Confidence = ordered[i].c,
                });
        }

        return orderedData;
    }

    /// <summary>
    /// Sets all six pockets in XZ (meters) and a table Y (meters).
    /// Order: TL, TR, ML, MR, BL, BR. X->Unity X, Z->Unity Z.
    /// Ignored if locked.
    /// </summary>
    private void PrivateSetPocketsXZ((float x, float z)[] pocketXZ, float tableY)
    {
        if (IsLockedToJitter) return;
        if (_externalPocketUpdatesSuppressed && !LockFinalized) return;
        if (pocketXZ?.Length != MAX_POCKET_COUNT) return;
        if (tableY <= 0f)
        {
            Debug.LogWarning("[TableService] PrivateSetPocketsXZ ignored because tableY is not valid yet.");
            return;
        }

        TableY = tableY;

        EnsureMarkers();

        float markerY = GetPocketMarkerY(tableY);

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            Vector3 worldPocketPosition = new(
                pocketXZ[i].x,
                markerY,
                pocketXZ[i].z
            );

            UpdatePocketPositionCache(i, worldPocketPosition);
            SetMarkerWorldPose(i, worldPocketPosition);
        }

        PocketCount = MAX_POCKET_COUNT;

        if (_lastMarkerPosition == null || _lastMarkerPosition.Length != MAX_POCKET_COUNT)
            _lastMarkerPosition = new Vector3[MAX_POCKET_COUNT];

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            _lastMarkerPosition[i] = _markers[i] != null
                ? _markers[i].transform.position
                : PocketPositions[i];
        }

        CaptureCurrentPocketEditingBasisFromCurrentPockets();

        Debug.Log($"[TableService] Pockets placed at markerY={markerY:F4}, TableY={TableY:F4}, BallDiameterM={BallDiameterM:F4}, PocketCount={PocketCount}");
    }

    public bool CanFinalizePocketPlacement() => IsTableHeightSet() && HasAllPockets() && _markers != null && _markers.Length == MAX_POCKET_COUNT;

    public void FinalizePocketPlacement()
    {
        if (!CanFinalizePocketPlacement())
        {
            Debug.LogWarning("[TableService] FinalizePocketPlacement ignored because the table state is not ready yet.");
            return;
        }

        IsLockedToJitter = true;
        LockFinalized = true;

        if (_lastMarkerPosition == null || _lastMarkerPosition.Length != MAX_POCKET_COUNT)
            _lastMarkerPosition = new Vector3[MAX_POCKET_COUNT];

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            if (_markers != null && i < _markers.Length && _markers[i] != null)
            {
                _lastMarkerPosition[i] = _markers[i].transform.position;
            }
            else if (PocketPositions != null && i < PocketPositions.Length)
            {
                _lastMarkerPosition[i] = PocketPositions[i];
            }
        }

        ApplyLockStateToMarkers();
        _externalPocketUpdatesSuppressed = false;

        Debug.Log("[TableService] Pocket placement finalized. Markers are frozen in place.");
    }

    public void ReEnablePocketEditing()
    {
        LockFinalized = false;
        IsLockedToJitter = false;

        CaptureCurrentPocketEditingBasisFromCurrentPockets();
        ApplyLockStateToMarkers();

        Debug.Log("[TableService] Pocket editing re-enabled.");
    }

    public void SetExternalPocketUpdatesSuppressed(bool suppressed)
    {
        _externalPocketUpdatesSuppressed = suppressed;
    }

    public void CaptureCurrentPocketEditingBasisFromCurrentPockets()
    {
        if (PocketPositions == null || PocketPositions.Length != MAX_POCKET_COUNT || !HasAllPockets())
            return;

        Vector3 topLeft = FlattenToTablePlane(PocketPositions[0]);
        Vector3 topRight = FlattenToTablePlane(PocketPositions[1]);
        Vector3 bottomMiddle = FlattenToTablePlane(PocketPositions[2]);
        Vector3 topMiddle = FlattenToTablePlane(PocketPositions[3]);
        Vector3 bottomLeft = FlattenToTablePlane(PocketPositions[4]);
        Vector3 bottomRight = FlattenToTablePlane(PocketPositions[5]);

        Vector3 longAxis = FlattenDirectionToTablePlane(((topRight - topLeft) + (bottomRight - bottomLeft)) * 0.5f);
        Vector3 shortAxis = FlattenDirectionToTablePlane(((topLeft - bottomLeft) + (topRight - bottomRight)) * 0.5f);

        if (longAxis.sqrMagnitude <= 0.000001f || shortAxis.sqrMagnitude <= 0.000001f)
            return;

        longAxis = longAxis.normalized;
        shortAxis = Vector3.Cross(Vector3.up, longAxis).normalized;

        if (Vector3.Dot(shortAxis, FlattenDirectionToTablePlane(topMiddle - bottomMiddle)) < 0f)
            shortAxis = -shortAxis;

        _pocketEditingBasisOrigin = 0.25f * (topLeft + topRight + bottomLeft + bottomRight);
        _pocketEditingLongAxis = longAxis;
        _pocketEditingShortAxis = shortAxis;
        _hasPocketEditingBasis = true;
    }


    //public void DetectMarker()
    public void SetMarkersBasedOnQRDetections()
    {
        if (PocketPositions?.Any(pp => pp == default) != false || PocketPositions.Length != MAX_POCKET_COUNT)
        {
            Debug.Log("The pockets have not yet been calculated.");
            return;
        }

        EnsureMarkers();

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            SetMarkerWorldPose(i, PocketPositions[i]);
        }

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            _lastMarkerPosition[i] = _markers[i].transform.position;
        }

        CaptureCurrentPocketEditingBasisFromCurrentPockets();
    }

    public void SetEdgeDiamonds((float x, float z, byte i, float c)[] diamonds) => PrivateSetEdgeDiamonds(diamonds, TableY);
    public void SetPocketsXZ((float x, float z)[] pocketXZ) => PrivateSetPocketsXZ(pocketXZ, TableY);
    public void EnsureMarkers()
    {
        if (_markers?.Length == MAX_POCKET_COUNT) return;
        _markers = new GameObject[MAX_POCKET_COUNT];

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
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

            // MODIFIED: initialize from the current world pose, but this baseline
            // will be refreshed again whenever TableService sets the real pocket pose.
            constraint.Initialize(go.transform.position, go.transform.rotation);

            go.name = $"PocketMarker_{i}";
            _markers[i] = go;
        }

        ApplyLockStateToMarkers();

        if (_lastMarkerPosition == null || _lastMarkerPosition.Length != MAX_POCKET_COUNT)
            _lastMarkerPosition = new Vector3[MAX_POCKET_COUNT];

        for (int i = 0; i < MAX_POCKET_COUNT; i++)
        {
            _lastMarkerPosition[i] = _markers[i] != null
                ? _markers[i].transform.position
              : Vector3.zero;
        }
    }

    public void SetBallDiameter(float ballDiameter)
    {
        BallDiameterM = ballDiameter > 0f ? ballDiameter : BallDiameterM;

        if (TableY > 0f && HasAllPockets())
            ReapplyPockets(TableY);

        SetBallHeight();
    }

    public void SetBallCircumference(float ballCircumference) => BallCircumferenceM = ballCircumference > 0f ? ballCircumference : BallCircumferenceM;
    public void SetTableLenght(float length) => SetTable(length, TableSize.y, TableY);
    public void SetTableWidth(float width) => SetTable(TableSize.x, width, TableY);
    public void SetTableHeight(float height) => SetTable(TableSize.x, TableSize.y, height);
    public void SetTable(Vector2 widthAndLength, float height) => SetTable(widthAndLength.x, widthAndLength.y, height);
    public void SetTable(Vector3 tableDimensions) => SetTable(tableDimensions.x, tableDimensions.z, tableDimensions.y);
    public void SetTable(Vector3Float tableDimensions) => SetTable(tableDimensions.X, tableDimensions.Z, tableDimensions.Y);
    public void SetTable(EnvironmentInfo env)
    {
        if (env?.Table == null) return;


        SetTable(env.Table.Length / 1000, env.Table.Width / 1000, env.Table.Height / 1000);
    }

    public void SetTable(float length, float width, float newTableY)
    {
        if (IsLockedToJitter) return;

        TableSize = new Vector2(
            length > 0 ? length : TableSize.x,
            width > 0 ? width : TableSize.y
        );

        bool tableHeightChanged = newTableY > 0f && !Mathf.Approximately(newTableY, TableY);

        if (newTableY > 0f)
            TableY = newTableY;

        if (tableHeightChanged)
            ReapplyPockets(TableY);

        SetBallHeight();
    }

    public void SetLocked(bool locked)
    {
        IsLockedToJitter = locked;
        ApplyLockStateToMarkers();
    }

    public void SetCamera(float cameraHeightFromFloor) => CameraHeightFromFloor = cameraHeightFromFloor;

    public void FinalizeLocked()
    {
        FinalizePocketPlacement();
        ProcessDiamonds(_diamondMarkerData);
    }

    public bool TrySaveEnviroment()
    {
        if (_enviromentSaved) return true;

        var info = new EnvironmentInfo()
        {
            Table = new Table(
                (short)(TableSize.x > TableSize.y ? TableSize.x : TableSize.y),
                (short)(TableSize.x > TableSize.y ? TableSize.y : TableSize.x),
                (short)TableY
            ),

            BallSpec = new BallSpec()
            {
                DiameterM = BallDiameterM,
                BallCircumferenceM = BallCircumferenceM
            },

            CameraData = new CameraData()
            {
                HeightFromFloorM = CameraHeightFromFloor
            }
        };

        AppSettings.Instance.Settings.EnviromentInfo = info;
        AppSettings.Instance.Save();

        _enviromentSaved = true;
        return _enviromentSaved;
    }

    public void PlaceBalls(float x, float y, byte id, float conf, float vx, float vy)
    {
        if (!TryMapIncomingBallToBallType(id, out BallType ballType))
            return;

        ApplyDetectedBallSnapshot(
            new[]
            {
            new IncomingDetectedBallRecord(
                ballType,
                id,
                new Vector2Float(x, y),
                conf,
                new Vector2Float(vx, vy))
            },
            replaceCurrentSnapshot: false);
    }

    //public void PlaceBalls(float x, float y, byte id, float conf, float vx, float vy)
    //{
    //    // ADDED: ISSUE-83 runs before any smoothing or later ball assignment logic.
    //    // This is intentionally a pre-placement suppression filter.
    //    CleanupExpiredNearPocketBallMemory();

    //    if (TryHandleNearPocketBall(x, y, id, conf, out PocketZoneBallState nearPocketState))
    //    {
    //        // ADDED: standard visualization / standard placement is intentionally suppressed here.
    //        // Near-pocket balls are represented only by debug markers at this stage.
    //        return;
    //    }

    //    // --------------------------------------------------------------------
    //    // EXISTING / FUTURE NORMAL BALL PLACEMENT PATH CONTINUES BELOW
    //    // --------------------------------------------------------------------
    //    // Keep your numbered-ball assignment, user override UI, smoothing, and
    //    // secondary Quest synchronization logic here. The important rule is:
    //    //
    //    //   issue #83 filter FIRST
    //    //   smoothing SECOND
    //    //   normal visualization / exercise logic AFTER THAT
    //    //
    //    // The current method body was intentionally incomplete already, so this
    //    // implementation only adds the required deterministic suppression layer.
    //    // --------------------------------------------------------------------

    //    // Based on the game mode add a variable lenght of array. Set the
    //    //if (AppSettings.Instance.GameMode == GameMode.LessonsMode)

    //    //else { }

    //    // If a ball was found and isn't in the buffer then add it later in the
    //    // normal placement path, once ball identity assignment is finished.

    //    if (AppSettings.Instance.Settings.DeviceInformation == DeviceInformation.PrimaryQuest)
    //    {
    //        // Send to other Quest 3 devices in the normal placement path later. # 
    //    }

    //    if ((id <= (byte)BallType.Cue))
    //    {
    //        // Normal placement / jitter distinction will stay here later.
    //    }
    //    else
    //    {
    //        // Generic solid/stripe handling stays here later.
    //    }

    //    // Create a buffer for a fluid system of tracking each ball for multiple points.
    //}

    //public void PlaceBalls(float x, float y, byte id, float conf, float vx, float vy)
    //{
    // Based on the game mode add a variable lenght of array. Set the 
    //if (AppSettings.Instance.GameMode == GameMode.LessonsMode)

    //else { }



    /* 
     * How to know which ball to place. For the "cue" and "eight" I know because of the the id.....hovewer what about the others (assumming I have only generic stripe and solid for now)?

    // Determine the ball type and location.
    // Go through each buffer range (for stripes and solids, for cue/eight we know the exaclty array positions) and compare the balls with the XZ locations already in there.
    // If the buffer has no balls
    //  Save immediately. Based on the type spawn a "pokeable" circle on it that opens a menu to assign a ball number (that hasn't been already assigned) based on the type. Cue and eight should be done automatically
    //  but suggest/add a way to recover from the error (change ball number). BONUS: Add a photo taking feature for future training, based on the pool ball set.
    //  
    // If there are balls already within a certain treshold. Ask the user if the ball matches an already detected ball.
    //  YES -> Overwrite it and log this event somehow for later visualization.
               Lock the index for the assigned ball so that 
    //         If the ball position was user overriden, then do not move it.
    //  NOT within treshold of another ball -> if there is place add it as a new ball of this type and number. If not ERROR?


    // Track the new saved balls and the updated balls. Should be 16 or less. 
        While the total amount of balls equal to python script ball data (send new data) keep the array of ball the same size. Else shrink it or mark
    Adjust the scenario  accordingly in the Word file
    */



    // If a ball was found and isn't in the buffer the add it.

    //No generic

    // At the end transfer the data to the secondary Quest.
    //if (AppSettings.Instance.Settings.DeviceInformation == DeviceInformation.PrimaryQuest)
    //{
    // Send to other Quest 3 devices.
    //}



    //if ((id <= (byte)BallType.Cue))
    //{
    // Work directly on the ball array.
    // Handle Jitter - distinguish between this and the actuall movement. Probably both the unity script and the python script should do this? Also multiple
    //}
    //else
    //{
    // Wtf should I do here?
    /*
     Idea:
     Accumulate 14 (we don't need it for cue and eightball) XY locations of balls. 
     Account for jitter and situation when two or more balls are next to one another.
     Determine whether they are solid or striped.
     For each ball, create a selection menu and make the user select the type (number).
     */
    //}

    // Create a buffer for a fluid system of tracking each ball for multiple points.
    //}

    public void ReapplyPockets(float tableY)
    {
        if (tableY <= 0f)
            return;

        if (_markers == null || _markers.Length != MAX_POCKET_COUNT)
            return;

        if (PocketCount == 0)
            return;

        bool wasLocked = IsLockedToJitter;
        if (wasLocked)
            IsLockedToJitter = false;

        TableY = tableY;
        float markerY = GetPocketMarkerY(tableY);

        for (byte i = 0; i < PocketPositions.Length; i++)
        {
            Vector3 p = PocketPositions[i];
            p.y = markerY;

            UpdatePocketPositionCache(i, p);
            SetMarkerWorldPose(i, p);
        }

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            _lastMarkerPosition[i] = _markers[i].transform.position;
        }

        CaptureCurrentPocketEditingBasisFromCurrentPockets();

        if (wasLocked)
            IsLockedToJitter = true;

        // Keep ball height refresh separate. Pocket markers do not depend on ball diameter.
        SetBallHeight();
    }

    public void ReapplyPockets()
    {
        if (TableY > 0)
            ReapplyPockets(TableY);
        SetBallHeight();
    }

    private void HandlePocketMarkers()
    {
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
            for (sbyte i = 0; i < MAX_POCKET_COUNT; i++)
                _lastMarkerPosition[i] = _markers[i].transform.position;
            return;
        }

        if (!MaintainPocketOrientationDuringEditing || !_hasPocketEditingBasis)
            CaptureCurrentPocketEditingBasisFromCurrentPockets();

        if (!_hasPocketEditingBasis)
        {
            for (sbyte i = 0; i < MAX_POCKET_COUNT; i++)
                _lastMarkerPosition[i] = _markers[i].transform.position;
            return;
        }

        Vector3 TL = FlattenToTablePlane(_markers[0].transform.position);
        Vector3 TR = FlattenToTablePlane(_markers[1].transform.position);
        Vector3 ML = FlattenToTablePlane(_markers[2].transform.position);
        Vector3 MR = FlattenToTablePlane(_markers[3].transform.position);
        Vector3 BL = FlattenToTablePlane(_markers[4].transform.position);
        Vector3 BR = FlattenToTablePlane(_markers[5].transform.position);

        float tlU = ProjectPocketLong(TL);
        float trU = ProjectPocketLong(TR);
        float mlU = ProjectPocketLong(ML);
        float mrU = ProjectPocketLong(MR);
        float blU = ProjectPocketLong(BL);
        float brU = ProjectPocketLong(BR);

        float tlV = ProjectPocketShort(TL);
        float trV = ProjectPocketShort(TR);
        float mlV = ProjectPocketShort(ML);
        float mrV = ProjectPocketShort(MR);
        float blV = ProjectPocketShort(BL);
        float brV = ProjectPocketShort(BR);

        float leftU = 0.5f * (tlU + blU);
        float rightU = 0.5f * (trU + brU);
        float topV = (tlV + trV + mrV) / 3f;
        float bottomV = (blV + brV + mlV) / 3f;

        switch (moved)
        {
            case 0: leftU = tlU; topV = tlV; break;
            case 1: rightU = trU; topV = trV; break;
            case 2: bottomV = mlV; break;
            case 3: topV = mrV; break;
            case 4: leftU = blU; bottomV = blV; break;
            case 5: rightU = brU; bottomV = brV; break;
        }

        float centerU = 0.5f * (leftU + rightU);
        float markerY = GetPocketMarkerY(TableY);

        TL = ComposePocketFromBasis(leftU, topV, markerY);
        TR = ComposePocketFromBasis(rightU, topV, markerY);
        BL = ComposePocketFromBasis(leftU, bottomV, markerY);
        BR = ComposePocketFromBasis(rightU, bottomV, markerY);
        ML = ComposePocketFromBasis(centerU, bottomV, markerY);
        MR = ComposePocketFromBasis(centerU, topV, markerY);

        SetMarkerWorldPose(0, TL);
        SetMarkerWorldPose(1, TR);
        SetMarkerWorldPose(2, ML);
        SetMarkerWorldPose(3, MR);
        SetMarkerWorldPose(4, BL);
        SetMarkerWorldPose(5, BR);

        UpdatePocketPositionCache(0, TL);
        UpdatePocketPositionCache(1, TR);
        UpdatePocketPositionCache(2, ML);
        UpdatePocketPositionCache(3, MR);
        UpdatePocketPositionCache(4, BL);
        UpdatePocketPositionCache(5, BR);

        for (sbyte i = 0; i < MAX_POCKET_COUNT; i++)
            _lastMarkerPosition[i] = _markers[i].transform.position;
    }

    private Vector3 FlattenToTablePlane(Vector3 world)
    {
        float y = TableY > 0f ? GetPocketMarkerY(TableY) : world.y;
        return new Vector3(world.x, y, world.z);
    }

    private Vector3 FlattenDirectionToTablePlane(Vector3 direction) => new(direction.x, 0f, direction.z);

    private float ProjectPocketLong(Vector3 world) => Vector3.Dot(FlattenToTablePlane(world) - _pocketEditingBasisOrigin, _pocketEditingLongAxis);

    private float ProjectPocketShort(Vector3 world) => Vector3.Dot(FlattenToTablePlane(world) - _pocketEditingBasisOrigin, _pocketEditingShortAxis);

    private Vector3 ComposePocketFromBasis(float longCoord, float shortCoord, float markerY)
    {
        Vector3 world = _pocketEditingBasisOrigin + (_pocketEditingLongAxis * longCoord) + (_pocketEditingShortAxis * shortCoord);
        world.y = markerY;
        return world;
    }

    private float GetDefaultBallHeight(float tableY)
    {
        if (tableY <= 0f) return tableY;
        if (BallDiameterM <= 0f) return tableY;
        return tableY + (BallDiameterM * 0.5f);
    }

    private float GetPocketMarkerY(float tableY) => tableY <= 0f ? tableY : tableY + PocketMarkerLiftAboveTableM;

    private void SetMarkerWorldPose(byte index, Vector3 worldPosition)
    {
        if (_markers == null || index >= _markers.Length)
            return;

        GameObject marker = _markers[index];
        if (marker == null)
            return;

        if (marker.TryGetComponent<XZOnlyConstraint>(out var constraint))
        {
            constraint.SetConstrainedWorldPose(worldPosition, Quaternion.identity);
        }
        else
        {
            marker.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        }
    }

    private void UpdatePocketPositionCache(byte index, Vector3 worldPosition)
    {
        if (PocketPositions == null || index >= PocketPositions.Length)
            return;

        PocketPositions[index] = worldPosition;
    }

    private void ApplyLockStateToMarkers()
    {
        if (_markers == null) return;

        bool editingEnabled = !IsLockedToJitter; // UPDATED: if locked, editing must be disabled

        foreach (var marker in _markers)
        {
            if (marker == null) continue;

            ApplyMarkerEditState(marker, editingEnabled); // UPDATED: freeze/unfreeze entire marker interaction stack
        }
    }

    private void ApplyMarkerEditState(GameObject marker, bool editingEnabled)
    {
        if (marker == null) return;

        // MODIFIED: always refresh the constrained baseline before changing edit state.
        if (marker.TryGetComponent<XZOnlyConstraint>(out var constraint))
        {
            constraint.SetConstrainedWorldPose(marker.transform.position, marker.transform.rotation);
            constraint.GrabbableEnabled = editingEnabled;
        }

        // MODIFIED: colliders must stay enabled while editing so Quest grab/ray/poke systems
        // can still target the marker. When locked, disable them to prevent accidental edits.
        Collider[] colliders = marker.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var collider in colliders)
        {
            if (collider == null) continue;
            collider.enabled = editingEnabled;
        }

        // MODIFIED: keep markers kinematic to prevent physics drift,
        // but keep collision participation enabled while editing so interaction still works.
        Rigidbody[] rigidbodies = marker.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        foreach (var rb in rigidbodies)
        {
            if (rb == null) continue;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.useGravity = false; // MODIFIED: markers must never fall
            rb.isKinematic = true; // MODIFIED: always kinematic, even while editing
            rb.detectCollisions = editingEnabled; // MODIFIED: interaction works while editing, disabled when locked
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete; // ADDED: stable enough for setup handles
            rb.interpolation = RigidbodyInterpolation.None; // ADDED: avoid extra smoothing noise

            rb.constraints = editingEnabled
                ? RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY // MODIFIED: allow only planar adjustment
                : RigidbodyConstraints.FreezeAll; // MODIFIED: fully lock after confirmation
        }

        // Keep Meta/Oculus interaction scripts enabled only while editing.
        Behaviour[] behaviours = marker.GetComponentsInChildren<Behaviour>(includeInactive: true);
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;

            if (MarkerInteractionComponentTypeNames.Contains(typeName))
            {
                behaviour.enabled = editingEnabled;
            }
        }
    }

    private void EnsureBallBufferSize()
    {
        _balls ??= new List<Vector3Float>(MAX_BALL_COUNT);

        while (_balls.Count < MAX_BALL_COUNT)
            _balls.Add(null);

        if (_balls.Count > MAX_BALL_COUNT)
            _balls.RemoveRange(MAX_BALL_COUNT, _balls.Count - MAX_BALL_COUNT);
    }

    private void SetBallHeight()
    {
        EnsureBallBufferSize();

        if (TableY <= 0f || BallDiameterM <= 0f)
            return;

        float h = GetDefaultBallHeight(TableY);

        for (byte i = 0; i < MAX_BALL_COUNT; i++)
        {
            if (_balls[i] == null)
                _balls[i] = new Vector3Float(h);
            else
                _balls[i].SetHeight(h);
        }
    }
    public void ResetNearPocketSuppressionState()
    {
        _nearPocketBallMemory.Clear();

        CueBallPocketed = false;
        EightBallPocketed = false;
        CueBallPocketIndex = byte.MaxValue;
        EightBallPocketIndex = byte.MaxValue;

        RefreshNearPocketDebugMarkers();
    }


    private bool TryHandleNearPocketBall(float x, float y, byte id, float conf, out PocketZoneBallState resolvedState)
    {
        resolvedState = PocketZoneBallState.Visible;

        if (!EnableNearPocketSuppression)
            return false;

        if (!HasAllPockets())
            return false;

        if (!TryMapIncomingBallToBallType(id, out BallType ballType))
            return false;

        Vector2Float positionXZ = new(x, y);

        byte nearestPocketIndex = byte.MaxValue;
        float nearestPocketSqrDistance = float.MaxValue;

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            Vector3 pocket = PocketPositions[i];

            float dx = x - pocket.x;
            float dz = y - pocket.z;
            float sqrDistance = (dx * dx) + (dz * dz);

            if (sqrDistance < nearestPocketSqrDistance)
            {
                nearestPocketSqrDistance = sqrDistance;
                nearestPocketIndex = i;
            }
        }

        if (nearestPocketIndex == byte.MaxValue)
            return false;

        bool isSpecialBall = IsSpecialBallType(ballType);
        NearPocketBallMemory existing = FindMatchingNearPocketMemory(ballType, nearestPocketIndex, positionXZ);

        PocketZoneBallState targetState = ResolvePocketZoneState(
            nearestPocketSqrDistance,
            existing,
            isSpecialBall);

        if (targetState == PocketZoneBallState.Visible)
        {
            // MODIFIED: remove transient memory once the ball has clearly left the suppression zone.
            if (existing != null && !existing.IsSpecialBall)
                _nearPocketBallMemory.Remove(existing);

            return false;
        }

        float distanceToPocketM = Mathf.Sqrt(nearestPocketSqrDistance);

        UpsertNearPocketBallMemory(
            existing,
            ballType,
            id,
            positionXZ,
            nearestPocketIndex,
            distanceToPocketM,
            conf,
            targetState,
            isSpecialBall);

        if (targetState == PocketZoneBallState.PocketedSpecialRetained)
            SetSpecialPocketedState(ballType, nearestPocketIndex);

        resolvedState = targetState;
        return true;
    }

    // ADDED: ISSUE-83 deterministic classification with hysteresis.
    private PocketZoneBallState ResolvePocketZoneState(
        float sqrDistanceToPocketM,
        NearPocketBallMemory existing,
        bool isSpecialBall)
    {
        float captureThresholdSqr = PocketCaptureThresholdM * PocketCaptureThresholdM;

        float ambiguousThresholdM = Mathf.Max(PocketAmbiguousThresholdM, PocketCaptureThresholdM);
        float ambiguousThresholdSqr = ambiguousThresholdM * ambiguousThresholdM;

        float ambiguousReleaseThresholdM = ambiguousThresholdM + PocketSuppressionReleaseMarginM;
        float ambiguousReleaseThresholdSqr = ambiguousReleaseThresholdM * ambiguousReleaseThresholdM;

        if (existing != null)
        {
            switch (existing.State)
            {
                case PocketZoneBallState.PocketedSuppressed:
                case PocketZoneBallState.PocketedSpecialRetained:
                    if (sqrDistanceToPocketM <= ambiguousReleaseThresholdSqr)
                    {
                        return isSpecialBall
                            ? PocketZoneBallState.PocketedSpecialRetained
                            : PocketZoneBallState.PocketedSuppressed;
                    }
                    break;

                case PocketZoneBallState.NearPocketAmbiguous:
                    if (sqrDistanceToPocketM <= ambiguousReleaseThresholdSqr)
                        return PocketZoneBallState.NearPocketAmbiguous;
                    break;
            }
        }

        if (TreatNearPocketZoneAsPocketed && sqrDistanceToPocketM <= captureThresholdSqr)
        {
            return isSpecialBall
                ? PocketZoneBallState.PocketedSpecialRetained
                : PocketZoneBallState.PocketedSuppressed;
        }

        if (sqrDistanceToPocketM <= ambiguousThresholdSqr)
            return PocketZoneBallState.NearPocketAmbiguous;

        return PocketZoneBallState.Visible;
    }

    // ADDED: ISSUE-83.
    private bool TryMapIncomingBallToBallType(byte id, out BallType ballType)
    {
        // Current wire format supports generic types directly.
        if (id <= (byte)BallType.Cue)
        {
            ballType = (BallType)id;
            return true;
        }

        // Future-proof fallback for numbered IDs.
        if (id >= 1 && id <= 7)
        {
            ballType = BallType.Solid;
            return true;
        }

        if (id == 8)
        {
            ballType = BallType.Eight;
            return true;
        }

        if (id >= 9 && id <= 15)
        {
            ballType = BallType.Stripe;
            return true;
        }

        ballType = default;
        return false;
    }

    // ADDED: ISSUE-83.
    private static bool IsSpecialBallType(BallType ballType)
    {
        return ballType == BallType.Cue || ballType == BallType.Eight;
    }

    // ADDED: ISSUE-83.
    private NearPocketBallMemory FindMatchingNearPocketMemory(BallType ballType, byte pocketIndex, Vector2Float positionXZ)
    {
        NearPocketBallMemory best = null;
        float bestSqrDistance = float.MaxValue;
        float maxSqrDistance = PocketSuppressionMatchRadiusM * PocketSuppressionMatchRadiusM;

        for (int i = 0; i < _nearPocketBallMemory.Count; i++)
        {
            NearPocketBallMemory candidate = _nearPocketBallMemory[i];

            if (candidate == null)
                continue;

            if (candidate.BallType != ballType)
                continue;

            if (candidate.PocketIndex != pocketIndex)
                continue;

            if (candidate.PositionXZ == null)
                continue;

            float dx = candidate.PositionXZ.X - positionXZ.X;
            float dz = candidate.PositionXZ.Y - positionXZ.Y;
            float sqrDistance = (dx * dx) + (dz * dz);

            if (sqrDistance <= maxSqrDistance && sqrDistance < bestSqrDistance)
            {
                best = candidate;
                bestSqrDistance = sqrDistance;
            }
        }

        return best;
    }

    private void UpsertNearPocketBallMemory(
        NearPocketBallMemory existing,
        BallType ballType,
        byte rawIncomingId,
        Vector2Float positionXZ,
        byte pocketIndex,
        float distanceToPocketM,
        float confidence,
        PocketZoneBallState state,
        bool isSpecialBall)
    {
        bool created = existing == null;

        if (created)
        {
            existing = new NearPocketBallMemory();
            _nearPocketBallMemory.Add(existing);
        }

        PocketZoneBallState previousState = existing.State;
        byte previousPocketIndex = existing.PocketIndex;

        existing.UpdateState(
            ballType,
            rawIncomingId,
            positionXZ,
            pocketIndex,
            distanceToPocketM,
            confidence,
            state,
            Time.time,
            isSpecialBall);

        if (VerboseNearPocketLogs && (created || previousState != state || previousPocketIndex != pocketIndex))
        {
            Debug.Log(
                $"[TableService][ISSUE-83] Ball suppressed. " +
                $"BallType={ballType}, RawId={rawIncomingId}, PocketIndex={pocketIndex}, " +
                $"State={state}, DistanceToPocketM={distanceToPocketM:F4}, Confidence={confidence:F3}");
        }
    }

    private void SetSpecialPocketedState(BallType ballType, byte pocketIndex)
    {
        switch (ballType)
        {
            case BallType.Cue:
                CueBallPocketed = true;
                CueBallPocketIndex = pocketIndex;
                break;

            case BallType.Eight:
                EightBallPocketed = true;
                EightBallPocketIndex = pocketIndex;
                break;
        }
    }

    private void CleanupExpiredNearPocketBallMemory()
    {
        if (_nearPocketBallMemory.Count == 0)
            return;

        float now = Time.time;

        for (int i = _nearPocketBallMemory.Count - 1; i >= 0; i--)
        {
            NearPocketBallMemory entry = _nearPocketBallMemory[i];

            if (entry == null)
            {
                _nearPocketBallMemory.RemoveAt(i);
                continue;
            }

            if (entry.IsSpecialBall)
                continue; // retained until ResetNearPocketSuppressionState()

            if ((now - entry.LastSeenTime) > PocketSuppressionMemorySeconds)
                _nearPocketBallMemory.RemoveAt(i);
        }
    }

    private float GetNearPocketDebugMarkerY()
    {
        return IsTableHeightSet()
            ? GetPocketMarkerY(TableY) + NearPocketDebugLift
            : transform.position.y + NearPocketDebugLift;
    }

    private void RefreshNearPocketDebugMarkers()
    {
        if (!ShowNearPocketRuntimeMarkers)
        {
            SetNearPocketDebugMarkersActive(false);
            return;
        }

        EnsureNearPocketDebugMarkers(_nearPocketBallMemory.Count);

        float y = GetNearPocketDebugMarkerY();

        for (int i = 0; i < _nearPocketBallMemory.Count; i++)
        {
            NearPocketBallMemory entry = _nearPocketBallMemory[i];
            GameObject marker = _runtimeNearPocketMarkers[i];

            if (entry == null || marker == null || entry.PositionXZ == null)
                continue;

            marker.transform.SetPositionAndRotation(
                new Vector3(entry.PositionXZ.X, y, entry.PositionXZ.Y),
                Quaternion.identity);

            ApplyNearPocketDebugColor(marker, GetNearPocketDebugColor(entry.State));
            marker.name = $"NearPocket_{entry.BallType}_{entry.PocketIndex}_{entry.State}";

            if (!marker.activeSelf)
                marker.SetActive(true);
        }

        for (int i = _nearPocketBallMemory.Count; i < _runtimeNearPocketMarkers.Count; i++)
            _runtimeNearPocketMarkers[i]?.SetActive(false);
    }

    private Color GetNearPocketDebugColor(PocketZoneBallState state)
    {
        return state switch
        {
            PocketZoneBallState.NearPocketAmbiguous => NearPocketAmbiguousColor,
            PocketZoneBallState.PocketedSuppressed => NearPocketPocketedColor,
            PocketZoneBallState.PocketedSpecialRetained => NearPocketSpecialColor,
            _ => Color.white
        };
    }

    private void EnsureNearPocketDebugMarkers(int requiredCount)
    {
        while (_runtimeNearPocketMarkers.Count < requiredCount)
        {
            _runtimeNearPocketMarkers.Add(CreateNearPocketDebugMarker(_runtimeNearPocketMarkers.Count));
        }

        for (int i = 0; i < _runtimeNearPocketMarkers.Count; i++)
        {
            if (_runtimeNearPocketMarkers[i] != null)
                _runtimeNearPocketMarkers[i].SetActive(i < requiredCount);
        }
    }

    private GameObject CreateNearPocketDebugMarker(int index)
    {
        Transform parent = NearPocketDebugMarkersParent != null
            ? NearPocketDebugMarkersParent
            : (MarkersParent != null ? MarkersParent : transform);

        GameObject go;

        if (NearPocketDebugMarkerPrefab != null)
        {
            go = Instantiate(NearPocketDebugMarkerPrefab, Vector3.zero, Quaternion.identity, parent);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            if (parent != null)
                go.transform.SetParent(parent, worldPositionStays: true);

            go.transform.localScale = Vector3.one * NearPocketDebugMarkerScale;
        }

        go.name = $"NearPocketDebug_{index}";

        Collider[] colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }

        Rigidbody[] rigidbodies = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        foreach (var rb in rigidbodies)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        ApplyNearPocketDebugColor(go, NearPocketAmbiguousColor);
        go.SetActive(false);

        return go;
    }

    private void ApplyNearPocketDebugColor(GameObject target, Color color)
    {
        if (target == null)
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            Material material = renderer.material;
            material.color = color;
        }
    }

    private void SetNearPocketDebugMarkersActive(bool active)
    {
        for (int i = 0; i < _runtimeNearPocketMarkers.Count; i++)
        {
            if (_runtimeNearPocketMarkers[i] != null && _runtimeNearPocketMarkers[i].activeSelf != active)
                _runtimeNearPocketMarkers[i].SetActive(active);
        }
    }

    private void DestroyNearPocketDebugMarkers()
    {
        for (int i = _runtimeNearPocketMarkers.Count - 1; i >= 0; i--)
        {
            GameObject marker = _runtimeNearPocketMarkers[i];
            if (marker == null)
                continue;

            if (Application.isPlaying)
                Destroy(marker);
            else
                DestroyImmediate(marker);
        }

        _runtimeNearPocketMarkers.Clear();
    }

    public void SetQrAlignmentConfirmedForBallVisualization(bool isConfirmed)
    {
        QrAlignmentConfirmedForBallVisualization = isConfirmed;
        TryApplyCachedDetectedBallSnapshot();
    }

    public bool IsReadyToVisualizeBalls()
    {
        if (!EnableBallVisualization)
            return false;

        if (!ArePropertiesParsed() || !HasAllPockets() || !LockFinalized)
            return false;

        if (RequireQrAlignmentBeforeBallVisualization &&
            !QrAlignmentConfirmedForBallVisualization &&
            !DebugBypassQrAlignmentGate)
        {
            return false;
        }

        return true;
    }

    public void ApplyDetectedBallSnapshot(IReadOnlyList<IncomingDetectedBallRecord> snapshot, bool replaceCurrentSnapshot = true)
    {
        if (snapshot == null || snapshot.Count == 0)
            return;

        if (replaceCurrentSnapshot)
            _latestDetectedBallSnapshot.Clear();

        for (int i = 0; i < snapshot.Count; i++)
        {
            IncomingDetectedBallRecord record = snapshot[i];
            if (record == null || record.PositionXZ == null)
                continue;

            _latestDetectedBallSnapshot.Add(record);
        }

        if (VerboseBallLogs)
        {
            Debug.Log(
                $"[TableService] Cached detector snapshot with {_latestDetectedBallSnapshot.Count} ball entries. " +
                $"ReadyForVisualization={IsReadyToVisualizeBalls()}");
        }

        TryApplyCachedDetectedBallSnapshot();
    }

    private void TryApplyCachedDetectedBallSnapshot()
    {
        if (!IsReadyToVisualizeBalls())
        {
            SetBallViewsActive(false);
            return;
        }

        if (_latestDetectedBallSnapshot.Count == 0)
            return;

        ApplyCachedDetectedBallSnapshotToRuntimeState();
        SyncBallViewsFromActiveTableState();
    }

    private void ApplyCachedDetectedBallSnapshotToRuntimeState()
    {
        TableStateEntry tableStateEntry = EnsureActiveTableStateEntry();
        if (tableStateEntry?.BallInfo == null)
            return;

        HashSet<Ball> consumedBalls = new();

        for (int i = 0; i < _latestDetectedBallSnapshot.Count; i++)
        {
            IncomingDetectedBallRecord record = _latestDetectedBallSnapshot[i];
            if (record == null || record.PositionXZ == null)
                continue;

            CleanupExpiredNearPocketBallMemory();

            if (TryHandleNearPocketBall(
                record.PositionXZ.X,
                record.PositionXZ.Y,
                record.RawIncomingId,
                record.Confidence,
                out _))
            {
                continue;
            }

            Ball runtimeBall = FindReusableBallForRecord(tableStateEntry.BallInfo, record, consumedBalls);

            if (runtimeBall == null)
            {
                if (tableStateEntry.BallInfo.Count >= MAX_BALL_COUNT)
                {
                    if (VerboseBallLogs)
                    {
                        Debug.LogWarning(
                            $"[TableService] Detector snapshot wants to create more than {MAX_BALL_COUNT} runtime balls. " +
                            $"Skipping extra ball of type {record.BallType}.");
                    }

                    continue;
                }

                runtimeBall = new Ball();
                tableStateEntry.BallInfo.Add(runtimeBall);
            }

            runtimeBall.ApplyDetectedState(record.BallType, record.RawIncomingId, record.PositionXZ);
            consumedBalls.Add(runtimeBall);
        }
    }

    private TableStateEntry EnsureActiveTableStateEntry()
    {
        if (_activeTableStateEntry != null)
            return _activeTableStateEntry;

        global::GameMode mode = global::GameMode.SoloGame;
        DeviceInformation deviceInformation = DeviceInformation.PrimaryQuest;

        if (AppSettings.Instance != null)
        {
            mode = AppSettings.Instance.GameMode;

            if (AppSettings.Instance.Settings != null)
                deviceInformation = AppSettings.Instance.Settings.DeviceInformation;
        }

        if (mode == global::GameMode.LessonsMode)
        {
            Debug.LogWarning(
                "[TableService] LessonsMode runtime ball state currently falls back to SoloGame " +
                "until exercise-id wiring is added to the runtime ball pipeline.");

            mode = global::GameMode.SoloGame;
        }

        _activeTableStateEntry = new TableStateEntry(mode, deviceInformation);
        _tableStateEntries ??= new List<TableStateEntry>();
        _tableStateEntries.Add(_activeTableStateEntry);

        return _activeTableStateEntry;
    }

    private Ball FindReusableBallForRecord(
        List<Ball> existingBalls,
        IncomingDetectedBallRecord record,
        HashSet<Ball> consumedBalls)
    {
        if (existingBalls == null || record == null || record.PositionXZ == null)
            return null;

        Ball bestMatch = null;
        float bestSqrDistance = float.MaxValue;
        float maxSqrDistance = BallReuseMatchRadiusM * BallReuseMatchRadiusM;

        for (int i = 0; i < existingBalls.Count; i++)
        {
            Ball candidate = existingBalls[i];
            if (candidate == null || consumedBalls.Contains(candidate))
                continue;

            if (candidate.GetLastDetectedOrCurrentBallType() != record.BallType)
                continue;

            Vector2Float candidatePosition = candidate.DetectedPosition ?? candidate.GetEffectivePosition();
            if (candidatePosition == null)
                continue;

            float sqrDistance = GetSqrDistance(candidatePosition, record.PositionXZ);

            if (record.BallType == BallType.Cue || record.BallType == BallType.Eight)
            {
                if (sqrDistance < bestSqrDistance)
                {
                    bestMatch = candidate;
                    bestSqrDistance = sqrDistance;
                }

                continue;
            }

            if (sqrDistance <= maxSqrDistance && sqrDistance < bestSqrDistance)
            {
                bestMatch = candidate;
                bestSqrDistance = sqrDistance;
            }
        }

        return bestMatch;
    }

    private static float GetSqrDistance(Vector2Float a, Vector2Float b)
    {
        if (a == null || b == null)
            return float.MaxValue;

        float dx = a.X - b.X;
        float dz = a.Y - b.Y;
        return (dx * dx) + (dz * dz);
    }

    private void SyncBallViewsFromActiveTableState()
    {
        if (_activeTableStateEntry?.BallInfo == null)
        {
            DestroyBallViews();
            return;
        }

        HashSet<Ball> liveBalls = new(_activeTableStateEntry.BallInfo.Where(ball => ball != null));

        foreach (KeyValuePair<Ball, GameObject> pair in _ballViewsByBall.ToArray())
        {
            if (liveBalls.Contains(pair.Key))
                continue;

            if (pair.Value != null)
                Destroy(pair.Value);

            _ballViewsByBall.Remove(pair.Key);
        }

        int visualIndex = 0;

        for (int i = 0; i < _activeTableStateEntry.BallInfo.Count; i++)
        {
            Ball runtimeBall = _activeTableStateEntry.BallInfo[i];
            if (runtimeBall == null)
                continue;

            if (!_ballViewsByBall.TryGetValue(runtimeBall, out GameObject ballView) || ballView == null)
            {
                ballView = CreateBallView(runtimeBall);
                _ballViewsByBall[runtimeBall] = ballView;
            }

            UpdateBallViewPose(runtimeBall, ballView, visualIndex);
            visualIndex++;
        }
    }

    private GameObject CreateBallView(Ball runtimeBall)
    {
        Transform parent = BallViewsParent != null
            ? BallViewsParent
            : (MarkersParent != null ? MarkersParent : transform);

        GameObject go;

        if (BallViewPrefab != null)
        {
            go = Instantiate(BallViewPrefab, Vector3.zero, Quaternion.identity, parent);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            if (parent != null)
                go.transform.SetParent(parent, worldPositionStays: true);

            if (go.TryGetComponent<Collider>(out Collider collider))
                Destroy(collider);

            float fallbackDiameter = BallDiameterM > 0f ? BallDiameterM : 0.05715f;
            go.transform.localScale = Vector3.one * fallbackDiameter;

            if (go.TryGetComponent<Renderer>(out Renderer renderer))
                renderer.material.color = GetFallbackBallColor(runtimeBall.BallType);
        }

        go.name = $"RuntimeBall_{_ballViewSequence++}";

        BallOverrideSelectable selectable = go.GetComponent<BallOverrideSelectable>();
        if (selectable == null)
            selectable = go.AddComponent<BallOverrideSelectable>();

        selectable.Bind(runtimeBall);
        go.SetActive(false);

        return go;
    }

    private void UpdateBallViewPose(Ball runtimeBall, GameObject ballView, int visualIndex)
    {
        if (runtimeBall == null || ballView == null)
            return;

        Vector2Float positionXZ = runtimeBall.GetEffectivePosition();
        if (positionXZ == null)
            return;

        float y = GetDefaultBallHeight(TableY) + BallViewSurfaceLiftM;

        ballView.transform.SetPositionAndRotation(
            new Vector3(positionXZ.X, y, positionXZ.Y),
            Quaternion.identity);

        BallOverrideSelectable selectable = ballView.GetComponent<BallOverrideSelectable>();
        if (selectable != null)
            selectable.Bind(runtimeBall);

        BallVisualView visualView = ballView.GetComponent<BallVisualView>();
        if (visualView != null)
            visualView.Bind(runtimeBall);

        ballView.name = $"RuntimeBall_{visualIndex}_{runtimeBall.BallType}_{runtimeBall.BallId}";
        ballView.SetActive(true); // UPDATED: ignored balls stay visible/selectable so the user can unignore them later
    }

    private Color GetFallbackBallColor(BallType ballType) =>
        ballType switch
        {
            BallType.Cue => Color.white,
            BallType.Eight => Color.black,
            BallType.Solid => new Color(0.95f, 0.8f, 0.1f, 1f),
            BallType.Stripe => new Color(0.2f, 0.45f, 1f, 1f),
            _ => Color.gray
        };

    private void SetBallViewsActive(bool active)
    {
        foreach (GameObject ballView in _ballViewsByBall.Values)
        {
            if (ballView != null && ballView.activeSelf != active)
                ballView.SetActive(active);
        }
    }

    private void DestroyBallViews()
    {
        foreach (GameObject ballView in _ballViewsByBall.Values.ToArray())
        {
            if (ballView == null)
                continue;

            if (Application.isPlaying)
                Destroy(ballView);
            else
                DestroyImmediate(ballView);
        }

        _ballViewsByBall.Clear();
    }

    public void ResetRuntimeBallOverridesAndRefreshViews()
    {
        _activeTableStateEntry?.ResetAllBallOverrides();
        SyncBallViewsFromActiveTableState();
    }

    public void ClearCachedDetectedBallSnapshot()
    {
        _latestDetectedBallSnapshot.Clear();
    }
}