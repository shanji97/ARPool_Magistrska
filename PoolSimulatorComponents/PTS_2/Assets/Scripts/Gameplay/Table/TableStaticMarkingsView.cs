using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class TableStaticMarkingsView : MonoBehaviour
{
    [Header("Dependencies")]
    public TableService TableServiceOverride;

    // move booleans into a single byte (make smaller memory size)
    [Header("Visibility")]
    public bool RequireAllPockets = true;

    [Tooltip("MODIFIED: markings now appear only after pocket placement is confirmed.")]
    public bool ShowOnlyAfterPocketPlacementConfirmed = true;

    [Tooltip("Keep markings visible after their own confirmation step.")]
    public bool ShowAfterMarkingsFinalize = true;

    [Header("Quarter Line")]
    public bool ShowQuarterLine = true;

    [Tooltip("MODIFIED: 1/4 line is measured from the long-axis table end, not the short-axis end.")]
    public TableReferenceEnd QuarterLineReferenceEnd = TableReferenceEnd.Left;

    [Tooltip("Inset from the long rails so the line does not run into pocket markers.")]
    public float QuarterLineRailInsetM = 0.08f;

    [Tooltip("MODIFIED: quarter line may be adjusted only along the long axis.")]
    public float QuarterLineLongitudinalOffsetM = 0f;

    public bool ShowQuarterLineAdjustmentMarker = true;

    [Header("Rack Guide")]
    public bool ShowRackGuide = true;

    [Tooltip("MODIFIED: rack apex is measured from the long-axis table end.")]
    public TableReferenceEnd RackApexReferenceEnd = TableReferenceEnd.Right;

    public float RackApexLongitudinalOffsetM = 0f;
    public float RackApexLateralOffsetM = 0f;

    [Tooltip("Extra outward expansion of the rack triangle.")]
    public float RackGuidePaddingM = 0.015f;

    [Tooltip("If true, the rack body extends from the apex toward the table center. This is the standard 8-ball layout.")]
    public bool RackExtendsTowardTableCenter = true; // ADDED

    [Tooltip("Signed long-axis shift applied after deterministic rack placement. Positive moves toward table Right, negative toward table Left.")]
    public float RackSignedLongAxisShiftM = 0f; // ADDED
    [ContextMenu("Reset Static Markings To Deterministic Preset")]
    public void ResetStaticMarkingsToDeterministicPreset()
    {
        ReEnableStaticMarkingsEditing(true);

        ResolveDependencies();

        if (_tableService == null)
        {
            Debug.LogWarning("[TableStaticMarkingsView] Reset requested, but TableService is not available.");
            return;
        }

        if (!ShouldShowMarkings())
        {
            Debug.LogWarning("[TableStaticMarkingsView] Reset requested, but markings are not currently available.");
            return;
        }

        if (!TryBuildGeometryBasis(out GeometryBasis basis))
        {
            Debug.LogWarning("[TableStaticMarkingsView] Reset requested, but geometry basis could not be built.");
            return;
        }

        EnsureAdjustmentMarkers();

        // MODIFIED: snap immediately instead of waiting for the next LateUpdate.
        if (_quarterLineHandleMarker != null)
        {
            SetAdjustmentMarkerWorldPose(
                _quarterLineHandleMarker,
                basis.DeterministicQuarterLineCenter,
                basis.MarkingY + AdjustmentMarkerLift);
            _quarterHandleInitialized = true;
        }

        if (_rackHandleMarker != null)
        {
            SetAdjustmentMarkerWorldPose(
                _rackHandleMarker,
                basis.DeterministicRackApex,
                basis.MarkingY + AdjustmentMarkerLift);
            _rackHandleInitialized = true;
        }

        Vector3 quarterLineCenter = GetQuarterLineCenterFromHandle(basis);
        Vector3 rackApex = GetRackApexFromHandle(basis);

        _currentGeometry = BuildFinalGeometryFromAdjustedCenters(basis, quarterLineCenter, rackApex);
        ApplyRuntimeVisuals(_currentGeometry);
        UpdateAdjustmentMarkerVisibility();

        Debug.Log("[TableStaticMarkingsView] Reset to deterministic preset applied immediately.");
    }

    public bool ShowRackAdjustmentMarker = true;

    [Header("Visuals")]
    public Material LineMaterial;

    [Tooltip("Optional prefab for the quarter-line and rack adjustment markers.")]
    public GameObject AdjustmentMarkerPrefab;

    public Transform AdjustmentMarkersParent;

    [Tooltip("Height above the cloth to avoid z-fighting.")]
    public float MarkingLift = 0.004f;

    [Tooltip("Extra lift for the interactive adjustment markers.")]
    public float AdjustmentMarkerLift = 0.02f;

    public float AdjustmentMarkerScale = 0.025f;
    public float LineWidth = 0.01f;

    public Color QuarterLineColor = Color.cyan;
    public Color RackGuideColor = Color.green;
    public Color QuarterLineMarkerColor = Color.cyan;
    public Color RackMarkerColor = Color.magenta;

    [Header("Editor Debug")]
    public bool ShowEditorGizmos = true;
    public bool ShowGeometryDebugLabels = false;

    private const float DefaultBallDiameterM = 0.05715f;

    private static readonly HashSet<string> MarkerInteractionComponentTypeNames = new()
    {
        "Grabbable",
        "HandGrabInteractable",
        "DistanceHandGrabInteractable",
        "RayInteractable",
        "PokeInteractable"
    };

    private TableService _tableService;
    private Material _runtimeLineMaterial;

    private LineRenderer _quarterLineRenderer;
    private LineRenderer _rackGuideRenderer;

    private GameObject _quarterLineHandleMarker;
    private GameObject _rackHandleMarker;

    private bool _quarterHandleInitialized;
    private bool _rackHandleInitialized;

    private TableReferenceGeometry _currentGeometry;

    public bool MarkingsFinalized { get; private set; }

    public bool TryGetCurrentTableReferenceGeometry(out TableReferenceGeometry geometry)
    {
        geometry = _currentGeometry;
        return geometry.IsValid;
    }

    public bool CanFinalizeStaticMarkingsPlacement()
    {
        return _tableService?.LockFinalized == true
            && _currentGeometry.IsValid;
    }

    public void FinalizeStaticMarkingsPlacement()
    {
        if (!CanFinalizeStaticMarkingsPlacement())
        {
            Debug.LogWarning("[TableStaticMarkingsView] FinalizeStaticMarkingsPlacement ignored because the geometry is not ready.");
            return;
        }

        MarkingsFinalized = true;

        ApplyAdjustmentMarkerEditState(_quarterLineHandleMarker, false);
        ApplyAdjustmentMarkerEditState(_rackHandleMarker, false);

        UpdateAdjustmentMarkerVisibility();

        Debug.Log("[TableStaticMarkingsView] Static markings finalized.");
    }

    public void ReEnableStaticMarkingsEditing(bool resetToDeterministic = false)
    {
        MarkingsFinalized = false;

        if (resetToDeterministic)
        {
            _quarterHandleInitialized = false; // MODIFIED: snap back to deterministic seed on next refresh
            _rackHandleInitialized = false;    // MODIFIED: snap back to deterministic seed on next refresh
        }

        ApplyAdjustmentMarkerEditState(_quarterLineHandleMarker, true);
        ApplyAdjustmentMarkerEditState(_rackHandleMarker, true);

        UpdateAdjustmentMarkerVisibility();

        Debug.Log("[TableStaticMarkingsView] Static markings editing re-enabled.");
    }

    private void Awake()
    {
        ResolveDependencies();
        EnsureRuntimeVisuals();
        HideAllRuntimeVisuals();
    }

    private void LateUpdate()
    {
        ResolveDependencies();
        EnsureRuntimeVisuals();
        RefreshRuntimeGeometryAndVisuals();
    }

    private void OnDestroy()
    {
        if (_runtimeLineMaterial != null)
        {
            Destroy(_runtimeLineMaterial);
            _runtimeLineMaterial = null;
        }
    }

    private void ResolveDependencies()
    {
        _tableService = TableServiceOverride ?? TableService.Instance;
    }

    private void RefreshRuntimeGeometryAndVisuals()
    {
        if (_tableService == null)
        {
            ResetMarkingsWorkflowState();
            HideAllRuntimeVisuals();
            _currentGeometry = default;
            return;
        }

        if (!ShouldShowMarkings())
        {
            if (!_tableService.LockFinalized)
            {
                // MODIFIED: when pockets are unlocked again, the static markings phase resets.
                ResetMarkingsWorkflowState();
            }

            HideAllRuntimeVisuals();
            _currentGeometry = default;
            return;
        }

        if (!TryBuildGeometryBasis(out GeometryBasis basis))
        {
            HideAllRuntimeVisuals();
            _currentGeometry = default;
            return;
        }

        EnsureAdjustmentMarkers();
        InitializeAdjustmentMarkersIfNeeded(basis);

        Vector3 quarterLineCenter = GetQuarterLineCenterFromHandle(basis);
        Vector3 rackApex = GetRackApexFromHandle(basis);

        _currentGeometry = BuildFinalGeometryFromAdjustedCenters(basis, quarterLineCenter, rackApex);

        ApplyRuntimeVisuals(_currentGeometry);
        UpdateAdjustmentMarkerVisibility();
    }

    private void ResetMarkingsWorkflowState()
    {
        MarkingsFinalized = false;
        _quarterHandleInitialized = false;
        _rackHandleInitialized = false;
    }

    private bool ShouldShowMarkings()
    {
        if (_tableService == null)
            return false;

        if (!_tableService.IsTableHeightSet())
            return false;

        if (RequireAllPockets && !_tableService.HasAllPockets())
            return false;

        if (ShowOnlyAfterPocketPlacementConfirmed && !_tableService.LockFinalized)
            return false;

        if (MarkingsFinalized && !ShowAfterMarkingsFinalize)
            return false;

        return true;
    }

    private bool TryBuildGeometryBasis(out GeometryBasis basis)
    {
        basis = default;

        if (_tableService == null)
            return false;

        Vector3[] pockets = _tableService.PocketPositions;
        if (pockets == null || pockets.Length != 6)
            return false;

        // Pocket order in this project:
        // 0 TL, 1 TR, 2 ML (bottom middle), 3 MR (top middle), 4 BL, 5 BR
        Vector3 tl = Flatten(pockets[0]);
        Vector3 tr = Flatten(pockets[1]);
        Vector3 bottomMiddle = Flatten(pockets[2]);
        Vector3 topMiddle = Flatten(pockets[3]);
        Vector3 bl = Flatten(pockets[4]);
        Vector3 br = Flatten(pockets[5]);

        if (tl == Vector3.zero || tr == Vector3.zero || bottomMiddle == Vector3.zero || topMiddle == Vector3.zero || bl == Vector3.zero || br == Vector3.zero)
            return false;

        float markingY = _tableService.TableY + MarkingLift;

        Vector3 leftShortRailCenter = SetY(0.5f * (tl + bl), markingY);
        Vector3 rightShortRailCenter = SetY(0.5f * (tr + br), markingY);
        Vector3 bottomLongRailCenter = SetY(0.5f * (bl + br), markingY);
        Vector3 topLongRailCenter = SetY(0.5f * (tl + tr), markingY);
        Vector3 center = SetY(0.5f * (leftShortRailCenter + rightShortRailCenter), markingY);

        // Long axis: Left -> Right
        Vector3 longAxisLeftToRight = Flatten(rightShortRailCenter - leftShortRailCenter).normalized;

        // Short axis: Bottom -> Top
        Vector3 shortAxisBottomToTop = Flatten(topLongRailCenter - bottomLongRailCenter).normalized;

        if (longAxisLeftToRight == Vector3.zero || shortAxisBottomToTop == Vector3.zero)
            return false;

        float tableLengthM = Vector3.Distance(leftShortRailCenter, rightShortRailCenter);
        float tableWidthM = Vector3.Distance(bottomLongRailCenter, topLongRailCenter);

        float quarterLineHalfWidthM = Mathf.Max(0.01f, (tableWidthM * 0.5f) - QuarterLineRailInsetM);

        float ballDiameterM = _tableService.BallDiameterM > 0f
            ? _tableService.BallDiameterM
            : DefaultBallDiameterM;

        Vector3 deterministicQuarterLineCenter = GetPointFromReferenceEnd(
            leftShortRailCenter,
            rightShortRailCenter,
            longAxisLeftToRight,
            QuarterLineReferenceEnd,
            (tableLengthM * 0.25f) + QuarterLineLongitudinalOffsetM);

        Vector3 deterministicRackApex = GetPointFromReferenceEnd(
            leftShortRailCenter,
            rightShortRailCenter,
            longAxisLeftToRight,
            RackApexReferenceEnd,
            (tableLengthM * 0.25f) + RackApexLongitudinalOffsetM);

        // ADDED: intuitive world-consistent long-axis shift.
        deterministicRackApex += longAxisLeftToRight * RackSignedLongAxisShiftM;

        // Existing lateral adjustment.
        deterministicRackApex += shortAxisBottomToTop * RackApexLateralOffsetM;

        // ADDED: decouple rack facing from rack reference end.
        Vector3 directionTowardTableCenterFromReferenceEnd =
            RackApexReferenceEnd == TableReferenceEnd.Left
                ? longAxisLeftToRight
                : -longAxisLeftToRight;

        Vector3 rackDepthDirection = RackExtendsTowardTableCenter
            ? directionTowardTableCenterFromReferenceEnd
            : -directionTowardTableCenterFromReferenceEnd;

        basis = new GeometryBasis
        {
            MarkingY = markingY,
            BallDiameterM = ballDiameterM,
            LeftShortRailCenter = leftShortRailCenter,
            RightShortRailCenter = rightShortRailCenter,
            BottomLongRailCenter = bottomLongRailCenter,
            TopLongRailCenter = topLongRailCenter,
            Center = center,
            LongAxisLeftToRight = longAxisLeftToRight,
            ShortAxisBottomToTop = shortAxisBottomToTop,
            TableLengthM = tableLengthM,
            TableWidthM = tableWidthM,
            QuarterLineHalfWidthM = quarterLineHalfWidthM,
            RackDepthDirection = rackDepthDirection,
            DeterministicQuarterLineCenter = SetY(deterministicQuarterLineCenter, markingY),
            DeterministicRackApex = SetY(deterministicRackApex, markingY)
        };

        return true;
    }

    private void InitializeAdjustmentMarkersIfNeeded(GeometryBasis basis)
    {
        if (_quarterLineHandleMarker != null && !_quarterHandleInitialized)
        {
            SetAdjustmentMarkerWorldPose(_quarterLineHandleMarker, basis.DeterministicQuarterLineCenter, basis.MarkingY + AdjustmentMarkerLift);
            _quarterHandleInitialized = true;
        }

        if (_rackHandleMarker != null && !_rackHandleInitialized)
        {
            SetAdjustmentMarkerWorldPose(_rackHandleMarker, basis.DeterministicRackApex, basis.MarkingY + AdjustmentMarkerLift);
            _rackHandleInitialized = true;
        }
    }

    private TableReferenceGeometry BuildFinalGeometryFromAdjustedCenters(
        GeometryBasis basis,
        Vector3 quarterLineCenter,
        Vector3 rackApex)
    {
        Vector3 quarterLineStart = quarterLineCenter - (basis.ShortAxisBottomToTop * basis.QuarterLineHalfWidthM);
        Vector3 quarterLineEnd = quarterLineCenter + (basis.ShortAxisBottomToTop * basis.QuarterLineHalfWidthM);

        float rowSpacingM = Mathf.Sqrt(3f) * 0.5f * basis.BallDiameterM;

        Vector3 baseCenter = rackApex + (basis.RackDepthDirection * (4f * rowSpacingM));
        Vector3 baseLeft = baseCenter - (basis.ShortAxisBottomToTop * (2f * basis.BallDiameterM));
        Vector3 baseRight = baseCenter + (basis.ShortAxisBottomToTop * (2f * basis.BallDiameterM));

        ExpandTriangle(ref rackApex, ref baseLeft, ref baseRight, RackGuidePaddingM);

        return new TableReferenceGeometry
        {
            IsValid = true,
            Center = basis.Center,
            LongAxisLeftToRight = basis.LongAxisLeftToRight,
            ShortAxisBottomToTop = basis.ShortAxisBottomToTop,
            TableLengthM = basis.TableLengthM,
            TableWidthM = basis.TableWidthM,
            QuarterLineCenter = SetY(quarterLineCenter, basis.MarkingY),
            QuarterLineStart = SetY(quarterLineStart, basis.MarkingY),
            QuarterLineEnd = SetY(quarterLineEnd, basis.MarkingY),
            RackApex = SetY(rackApex, basis.MarkingY),
            RackBaseLeft = SetY(baseLeft, basis.MarkingY),
            RackBaseRight = SetY(baseRight, basis.MarkingY)
        };
    }

    private Vector3 GetQuarterLineCenterFromHandle(GeometryBasis basis)
    {
        Vector3 result = basis.DeterministicQuarterLineCenter;

        if (_quarterLineHandleMarker == null)
            return result;

        Vector3 current = Flatten(_quarterLineHandleMarker.transform.position);

        // MODIFIED: quarter line may move ONLY along the long axis.
        float distanceFromLeft = Mathf.Clamp(
            Vector3.Dot(current - basis.LeftShortRailCenter, basis.LongAxisLeftToRight),
            0f,
            basis.TableLengthM);

        result = basis.LeftShortRailCenter + (basis.LongAxisLeftToRight * distanceFromLeft);
        result = SetY(result, basis.MarkingY);

        SetAdjustmentMarkerWorldPose(_quarterLineHandleMarker, result, basis.MarkingY + AdjustmentMarkerLift);

        return result;
    }

    private Vector3 GetRackApexFromHandle(GeometryBasis basis)
    {
        Vector3 result = basis.DeterministicRackApex;

        if (_rackHandleMarker == null)
            return result;

        Vector3 current = Flatten(_rackHandleMarker.transform.position);
        Vector3 deltaFromCenter = current - basis.Center;

        float halfLength = Mathf.Max(0.05f, (basis.TableLengthM * 0.5f) - basis.BallDiameterM);
        float halfWidth = Mathf.Max(0.05f, (basis.TableWidthM * 0.5f) - basis.BallDiameterM);

        float localLong = Mathf.Clamp(Vector3.Dot(deltaFromCenter, basis.LongAxisLeftToRight), -halfLength, halfLength);
        float localShort = Mathf.Clamp(Vector3.Dot(deltaFromCenter, basis.ShortAxisBottomToTop), -halfWidth, halfWidth);

        // MODIFIED: rack apex may move across the XZ plane.
        result = basis.Center
            + (basis.LongAxisLeftToRight * localLong)
            + (basis.ShortAxisBottomToTop * localShort);

        result = SetY(result, basis.MarkingY);

        SetAdjustmentMarkerWorldPose(_rackHandleMarker, result, basis.MarkingY + AdjustmentMarkerLift);

        return result;
    }

    private void EnsureRuntimeVisuals()
    {
        if (_quarterLineRenderer == null)
            _quarterLineRenderer = CreateLineRenderer("QuarterLineRenderer", QuarterLineColor);

        if (_rackGuideRenderer == null)
            _rackGuideRenderer = CreateLineRenderer("RackGuideRenderer", RackGuideColor);

        ApplyRendererStyle(_quarterLineRenderer, QuarterLineColor);
        ApplyRendererStyle(_rackGuideRenderer, RackGuideColor);
    }

    private void EnsureAdjustmentMarkers()
    {
        if (_quarterLineHandleMarker == null)
            _quarterLineHandleMarker = CreateAdjustmentMarker("QuarterLineHandleMarker", QuarterLineMarkerColor);

        if (_rackHandleMarker == null)
            _rackHandleMarker = CreateAdjustmentMarker("RackHandleMarker", RackMarkerColor);
    }

    private void ApplyRuntimeVisuals(TableReferenceGeometry geometry)
    {
        if (ShowQuarterLine)
        {
            _quarterLineRenderer.enabled = true;
            _quarterLineRenderer.positionCount = 2;
            _quarterLineRenderer.SetPosition(0, geometry.QuarterLineStart);
            _quarterLineRenderer.SetPosition(1, geometry.QuarterLineEnd);
        }
        else
        {
            _quarterLineRenderer.enabled = false;
        }

        if (ShowRackGuide)
        {
            _rackGuideRenderer.enabled = true;
            _rackGuideRenderer.positionCount = 4;
            _rackGuideRenderer.SetPosition(0, geometry.RackApex);
            _rackGuideRenderer.SetPosition(1, geometry.RackBaseLeft);
            _rackGuideRenderer.SetPosition(2, geometry.RackBaseRight);
            _rackGuideRenderer.SetPosition(3, geometry.RackApex);
        }
        else
        {
            _rackGuideRenderer.enabled = false;
        }
    }

    private void UpdateAdjustmentMarkerVisibility()
    {
        bool quarterVisible = ShowQuarterLine
            && ShowQuarterLineAdjustmentMarker
            && !MarkingsFinalized
            && ShouldShowMarkings();

        bool rackVisible = ShowRackGuide
            && ShowRackAdjustmentMarker
            && !MarkingsFinalized
            && ShouldShowMarkings();

        if (_quarterLineHandleMarker != null)
        {
            _quarterLineHandleMarker.SetActive(quarterVisible);
            ApplyAdjustmentMarkerEditState(_quarterLineHandleMarker, quarterVisible);
        }

        if (_rackHandleMarker != null)
        {
            _rackHandleMarker.SetActive(rackVisible);
            ApplyAdjustmentMarkerEditState(_rackHandleMarker, rackVisible);
        }
    }

    private LineRenderer CreateLineRenderer(string objectName, Color color)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        LineRenderer renderer = go.AddComponent<LineRenderer>();
        renderer.useWorldSpace = true;
        renderer.loop = false;
        renderer.positionCount = 0;
        renderer.widthMultiplier = LineWidth;
        renderer.numCapVertices = 4;
        renderer.numCornerVertices = 4;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = LineAlignment.View;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.material = GetOrCreateLineMaterial();
        renderer.startColor = color;
        renderer.endColor = color;
        renderer.enabled = false;

        return renderer;
    }

    private void ApplyRendererStyle(LineRenderer renderer, Color color)
    {
        if (renderer == null)
            return;

        renderer.widthMultiplier = LineWidth;
        renderer.startColor = color;
        renderer.endColor = color;
        renderer.material = GetOrCreateLineMaterial();
    }

    private Material GetOrCreateLineMaterial()
    {
        if (LineMaterial != null)
            return LineMaterial;

        if (_runtimeLineMaterial != null)
            return _runtimeLineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[TableStaticMarkingsView] Could not find Sprites/Default shader.");
            return null;
        }

        _runtimeLineMaterial = new Material(shader)
        {
            name = "TableStaticMarkings_RuntimeMaterial"
        };

        return _runtimeLineMaterial;
    }

    private GameObject CreateAdjustmentMarker(string objectName, Color color)
    {
        Transform parent = AdjustmentMarkersParent != null ? AdjustmentMarkersParent : transform;

        GameObject go;
        if (AdjustmentMarkerPrefab != null)
        {
            go = Instantiate(AdjustmentMarkerPrefab, parent);
            go.name = objectName;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = objectName;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * AdjustmentMarkerScale;
        }

        if (!go.TryGetComponent<XZOnlyConstraint>(out var constraint))
            constraint = go.AddComponent<XZOnlyConstraint>();

        constraint.Initialize(go.transform.position, go.transform.rotation);

        ApplyMarkerColor(go, color);
        ApplyAdjustmentMarkerEditState(go, false);

        go.SetActive(false);
        return go;
    }

    private void ApplyMarkerColor(GameObject target, Color color)
    {
        if (target == null)
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Material material = renderer.material;
            material.color = color;
        }
    }

    private void ApplyAdjustmentMarkerEditState(GameObject marker, bool editingEnabled)
    {
        if (marker == null)
            return;

        if (marker.TryGetComponent<XZOnlyConstraint>(out var constraint))
        {
            constraint.SetConstrainedWorldPose(marker.transform.position, marker.transform.rotation);
            constraint.GrabbableEnabled = editingEnabled;
        }

        Collider[] colliders = marker.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;
            collider.enabled = editingEnabled;
        }

        Rigidbody[] rigidbodies = marker.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        foreach (Rigidbody rigidbody in rigidbodies)
        {
            if (rigidbody == null) continue;

            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = editingEnabled;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rigidbody.interpolation = RigidbodyInterpolation.None;
            rigidbody.constraints = editingEnabled
                ? RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY
                : RigidbodyConstraints.FreezeAll;
        }

        Behaviour[] behaviours = marker.GetComponentsInChildren<Behaviour>(includeInactive: true);
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;
            if (MarkerInteractionComponentTypeNames.Contains(typeName))
            {
                behaviour.enabled = editingEnabled;
            }
        }
    }

    private void SetAdjustmentMarkerWorldPose(GameObject marker, Vector3 worldPosition, float y)
    {
        if (marker == null)
            return;

        worldPosition.y = y;

        if (marker.TryGetComponent<XZOnlyConstraint>(out var constraint))
        {
            constraint.SetConstrainedWorldPose(worldPosition, Quaternion.identity);
        }
        else
        {
            marker.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        }
    }

    private void HideAllRuntimeVisuals()
    {
        if (_quarterLineRenderer != null)
            _quarterLineRenderer.enabled = false;

        if (_rackGuideRenderer != null)
            _rackGuideRenderer.enabled = false;

        _quarterLineHandleMarker?.SetActive(false);

        _rackHandleMarker?.SetActive(false);
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static Vector3 SetY(Vector3 value, float y)
    {
        value.y = y;
        return value;
    }

    private static Vector3 GetPointFromReferenceEnd(
        Vector3 leftShortRailCenter,
        Vector3 rightShortRailCenter,
        Vector3 longAxisLeftToRight,
        TableReferenceEnd referenceEnd,
        float offsetFromReferenceEndM)
    {
        if (referenceEnd == TableReferenceEnd.Left)
            return leftShortRailCenter + (longAxisLeftToRight * offsetFromReferenceEndM);

        return rightShortRailCenter - (longAxisLeftToRight * offsetFromReferenceEndM);
    }

    private static void ExpandTriangle(ref Vector3 a, ref Vector3 b, ref Vector3 c, float paddingM)
    {
        if (paddingM <= 0f)
            return;

        Vector3 centroid = (a + b + c) / 3f;

        a = ExpandPointAwayFromCentroid(a, centroid, paddingM);
        b = ExpandPointAwayFromCentroid(b, centroid, paddingM);
        c = ExpandPointAwayFromCentroid(c, centroid, paddingM);
    }

    private static Vector3 ExpandPointAwayFromCentroid(Vector3 point, Vector3 centroid, float paddingM)
    {
        Vector3 direction = point - centroid;
        float magnitude = direction.magnitude;

        if (magnitude <= Mathf.Epsilon)
            return point;

        return point + (direction / magnitude) * paddingM;
    }


    private void OnDrawGizmos()
    {
        if (!ShowEditorGizmos)
            return;

        if (!_currentGeometry.IsValid)
            return;

        Gizmos.color = QuarterLineColor;
        Gizmos.DrawLine(_currentGeometry.QuarterLineStart, _currentGeometry.QuarterLineEnd);

        Gizmos.color = RackGuideColor;
        Gizmos.DrawLine(_currentGeometry.RackApex, _currentGeometry.RackBaseLeft);
        Gizmos.DrawLine(_currentGeometry.RackBaseLeft, _currentGeometry.RackBaseRight);
        Gizmos.DrawLine(_currentGeometry.RackBaseRight, _currentGeometry.RackApex);

        Gizmos.color = RackMarkerColor;
        Gizmos.DrawSphere(_currentGeometry.RackApex, 0.01f);

#if UNITY_EDITOR
        if (ShowGeometryDebugLabels)
        {
            UnityEditor.Handles.Label(_currentGeometry.Center + Vector3.up * 0.03f, "Table Center");
            UnityEditor.Handles.Label(_currentGeometry.QuarterLineCenter + Vector3.up * 0.03f, "1/4 Line Center");
            UnityEditor.Handles.Label(_currentGeometry.RackApex + Vector3.up * 0.03f, "Rack Apex");
        }
#endif
    }
}