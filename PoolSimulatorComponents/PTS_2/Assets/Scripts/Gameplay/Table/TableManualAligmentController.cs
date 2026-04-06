using UnityEngine;

public class TableManualAlignmentController : MonoBehaviour
{
    private const int TopLeftAnchorIndex = 0;
    private const int BottomRightAnchorIndex = 1;
    private const int BottomLeftAnchorIndex = 2; // UPDATED: third corner anchor removes mirror ambiguity

    [Header("Dependencies")]
    [SerializeField] private TableService tableService;
    [SerializeField] private Transform playerHead; // Assign: [BuildingBlock] Camera Rig / TrackingSpace / CenterEyeAnchor

    [Header("Anchor Spawn")]
    [SerializeField] private float spawnForwardDistanceM = 0.8f;
    [SerializeField] private float spawnSideOffsetM = 0.25f;
    [SerializeField] private float anchorLiftAboveTableM = 0.01f;

    [Header("Behaviour")]
    [SerializeField] private bool autoBeginOnEnable = false;
    [SerializeField] private bool destroyAnchorMarkersAfterPocketGeneration = true;
    [SerializeField] private bool verboseLogs = true;

    public AlignmentState State { get; private set; } = AlignmentState.Idle;
    public bool HasPlacedTopLeft => _anchors[TopLeftAnchorIndex] != null;
    public bool HasPlacedBottomRight => _anchors[BottomRightAnchorIndex] != null;
    public bool HasPlacedBottomLeft => _anchors[BottomLeftAnchorIndex] != null;
    public bool PythonFrameAligned => _pythonFrameAligned;
    public string StatusText => BuildStatusText();
    public string NextActionButtonText => BuildNextActionButtonText();

    private readonly GameObject[] _anchors = new GameObject[3]; // UPDATED: sequential 3-anchor workflow
    private bool _pythonFrameAligned;

    private void OnEnable()
    {
        ResolveDependencies();

        if (autoBeginOnEnable)
            BeginPlacingBottomRightAnchor();
    }

    public void AdvancePlacementStep()
    {
        switch (State)
        {
            case AlignmentState.Idle:
                BeginPlacingBottomRightAnchor();
                break;

            case AlignmentState.PlacingBottomRightCorner:
                ConfirmBottomRightAnchorPlacement(); // UPDATED: pause and confirm before spawning the next anchor
                break;

            case AlignmentState.BottomRightCornerPlaced:
                SpawnSecondTopLeftAnchor(); // UPDATED: second anchor is spawned only after the first one is confirmed
                break;

            case AlignmentState.PlacingTopLeftCorner:
                ConfirmTopLeftAnchorPlacement(); // UPDATED: pause and confirm before spawning the third anchor
                break;

            case AlignmentState.TopLeftCornerPlaced:
                SpawnThirdBottomLeftAnchor(); // UPDATED: third anchor is spawned only after the second one is confirmed
                break;

            case AlignmentState.PlacingBottomLeftCorner:
                ConfirmBottomLeftAnchorPlacement(); // UPDATED: pause and confirm before generating Quest pockets
                break;

            case AlignmentState.BottomLeftCornerPlaced:
                ConfirmAnchorPlacementAndGeneratePockets();
                break;

            case AlignmentState.EditingGeneratedPockets:
                if (!_pythonFrameAligned)
                    AlignPythonFrameWithQuestFrame();
                else
                    ConfirmGeneratedPockets();
                break;

            case AlignmentState.Confirmed:
                if (verboseLogs)
                    Debug.Log("[TableManualAlignmentController] Alignment is already confirmed.");
                break;
        }
    }

    public void BeginPlacingBottomRightAnchor()
    {
        ResolveDependencies();

        if (!CanStartManualAlignment())
            return;

        ResetAlignmentSession();

        Vector3 spawnPosition = BuildFirstAnchorSpawnPoint();
        CreateAnchor(BottomRightAnchorIndex, "ManualAlignment_BottomRightAnchor", spawnPosition);

        State = AlignmentState.PlacingBottomRightCorner;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Bottom-right anchor spawned. Move it to the real corner pocket, then confirm it.");
    }

    public void ConfirmBottomRightAnchorPlacement()
    {
        if (!TryGetAnchorPosition(BottomRightAnchorIndex, out _))
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot confirm the bottom-right anchor because it does not exist yet.");
            return;
        }

        State = AlignmentState.BottomRightCornerPlaced;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Bottom-right anchor confirmed.");
    }

    public void SpawnSecondTopLeftAnchor()
    {
        ResolveDependencies();

        if (tableService == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot spawn the top-left anchor because TableService is missing.");
            return;
        }

        if (_anchors[BottomRightAnchorIndex] == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot spawn the top-left anchor because the bottom-right anchor does not exist yet.");
            return;
        }

        if (_anchors[TopLeftAnchorIndex] != null)
        {
            Debug.LogWarning("[TableManualAlignmentController] The top-left anchor already exists.");
            return;
        }

        Vector3 spawnPosition = BuildSecondAnchorApproximateSpawnPoint(_anchors[BottomRightAnchorIndex].transform.position);
        CreateAnchor(TopLeftAnchorIndex, "ManualAlignment_TopLeftAnchor", spawnPosition);

        State = AlignmentState.PlacingTopLeftCorner;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Top-left anchor spawned. Move it to the opposite corner pocket, then confirm it.");
    }

    public void ConfirmTopLeftAnchorPlacement()
    {
        if (!TryGetAnchorPosition(TopLeftAnchorIndex, out _))
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot confirm the top-left anchor because it does not exist yet.");
            return;
        }

        State = AlignmentState.TopLeftCornerPlaced;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Top-left anchor confirmed.");
    }

    public void SpawnThirdBottomLeftAnchor()
    {
        ResolveDependencies();

        if (tableService == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot spawn the bottom-left anchor because TableService is missing.");
            return;
        }

        if (_anchors[BottomRightAnchorIndex] == null || _anchors[TopLeftAnchorIndex] == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot spawn the bottom-left anchor because the first two anchors are not ready yet.");
            return;
        }

        if (_anchors[BottomLeftAnchorIndex] != null)
        {
            Debug.LogWarning("[TableManualAlignmentController] The bottom-left anchor already exists.");
            return;
        }

        Vector3 spawnPosition = BuildThirdBottomLeftAnchorApproximateSpawnPoint(
            _anchors[TopLeftAnchorIndex].transform.position,
            _anchors[BottomRightAnchorIndex].transform.position);

        CreateAnchor(BottomLeftAnchorIndex, "ManualAlignment_BottomLeftAnchor", spawnPosition);

        State = AlignmentState.PlacingBottomLeftCorner;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Bottom-left anchor spawned. Move it to the remaining near-side corner pocket, then confirm it.");
    }

    public void ConfirmBottomLeftAnchorPlacement()
    {
        if (!TryGetAnchorPosition(BottomLeftAnchorIndex, out _))
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot confirm the bottom-left anchor because it does not exist yet.");
            return;
        }

        State = AlignmentState.BottomLeftCornerPlaced;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Bottom-left anchor confirmed.");
    }

    public void ConfirmAnchorPlacementAndGeneratePockets()
    {
        ResolveDependencies();

        if (tableService == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot generate Quest pockets because TableService is missing.");
            return;
        }

        if (!tableService.IsTableHeightSet() || !tableService.Is2DTableSet())
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot generate Quest pockets because TableService does not yet have TableY and TableSize.");
            return;
        }

        if (!TryGetAnchorPosition(TopLeftAnchorIndex, out Vector3 topLeft))
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot generate Quest pockets because the top-left anchor is missing.");
            return;
        }

        if (!TryGetAnchorPosition(BottomRightAnchorIndex, out Vector3 bottomRight))
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot generate Quest pockets because the bottom-right anchor is missing.");
            return;
        }

        if (!TryGetAnchorPosition(BottomLeftAnchorIndex, out Vector3 bottomLeft))
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot generate Quest pockets because the bottom-left anchor is missing.");
            return;
        }

        topLeft = FlattenToTablePlane(topLeft, tableService.TableY);
        bottomRight = FlattenToTablePlane(bottomRight, tableService.TableY);
        bottomLeft = FlattenToTablePlane(bottomLeft, tableService.TableY);

        if (!TryBuildPocketLayoutFromThreeCorners(
                topLeft,
                bottomRight,
                bottomLeft,
                tableService.TableSize.x,
                tableService.TableSize.y,
                out Vector3[] questPocketWorldPositions))
        {
            Debug.LogWarning("[TableManualAlignmentController] Quest pocket generation failed. Check the three anchor positions and table dimensions.");
            return;
        }

        tableService.SetExternalPocketUpdatesSuppressed(true);
        tableService.SetQuestPocketWorldPositions(questPocketWorldPositions);
        tableService.ReEnablePocketEditing();

        SetAnchorEditingEnabled(false);

        if (destroyAnchorMarkersAfterPocketGeneration)
            DestroyAnchorMarkers();

        _pythonFrameAligned = false;
        State = AlignmentState.EditingGeneratedPockets;

        if (verboseLogs)
        {
            Debug.Log(
                "[TableManualAlignmentController] Quest pockets generated from three anchors. " +
                $"TableLength={tableService.TableSize.x:F3}m, TableWidth={tableService.TableSize.y:F3}m.");
        }
    }

    public void AlignPythonFrameWithQuestFrame()
    {
        ResolveDependencies();

        if (tableService == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot align the Python frame because TableService is missing.");
            return;
        }

        bool aligned = tableService.ForceReprojectLatestPythonSnapshot();
        if (!aligned)
        {
            Debug.LogWarning("[TableManualAlignmentController] Python frame alignment did not run because there is no usable raw Python snapshot yet.");
            return;
        }

        _pythonFrameAligned = true;
        State = AlignmentState.EditingGeneratedPockets;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Python frame reprojected into the current Quest table frame.");
    }

    public void ConfirmGeneratedPockets()
    {
        ResolveDependencies();

        if (tableService == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot confirm pocket placement because TableService is missing.");
            return;
        }

        if (!tableService.CanFinalizePocketPlacement())
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot confirm pocket placement because TableService is not ready yet.");
            return;
        }

        tableService.FinalizePocketPlacement();
        State = AlignmentState.Confirmed;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Quest pocket alignment confirmed. Markers are now frozen.");
    }

    public void ResetAlignmentSession()
    {
        DestroyAnchorMarkers();

        _pythonFrameAligned = false;

        if (tableService != null)
            tableService.SetExternalPocketUpdatesSuppressed(false);

        State = AlignmentState.Idle;

        if (verboseLogs)
            Debug.Log("[TableManualAlignmentController] Manual alignment session reset.");
    }

    private void ResolveDependencies()
    {
        tableService ??= TableService.Instance;

        if (playerHead == null && Camera.main != null)
            playerHead = Camera.main.transform;
    }

    private bool CanStartManualAlignment()
    {
        if (tableService == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot start manual alignment because TableService is missing.");
            return false;
        }

        if (!tableService.IsTableHeightSet())
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot start manual alignment because TableY is not set yet.");
            return false;
        }

        if (!tableService.Is2DTableSet())
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot start manual alignment because TableSize is not set yet.");
            return false;
        }

        if (playerHead == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot start manual alignment because playerHead is missing.");
            return false;
        }

        if (tableService.PocketMarkerPrefab == null)
        {
            Debug.LogWarning("[TableManualAlignmentController] Cannot start manual alignment because TableService.PocketMarkerPrefab is missing.");
            return false;
        }

        return true;
    }

    private void CreateAnchor(int anchorIndex, string anchorName, Vector3 spawnPosition)
    {
        GameObject markerObject = Instantiate(tableService.PocketMarkerPrefab, spawnPosition, Quaternion.identity, transform);
        markerObject.name = anchorName;

        XZOnlyConstraint constraint = markerObject.GetComponent<XZOnlyConstraint>();
        if (constraint == null)
        {
            Debug.LogError($"[TableManualAlignmentController] Prefab '{tableService.PocketMarkerPrefab.name}' must already contain XZOnlyConstraint.");
            Destroy(markerObject);
            return;
        }

        ApplyCornerAnchorDiameter(markerObject);

        constraint.GrabbableEnabled = true;
        constraint.Initialize(markerObject.transform.position, markerObject.transform.rotation);

        _anchors[anchorIndex] = markerObject;
    }

    private void ApplyCornerAnchorDiameter(GameObject markerObject)
    {
        if (markerObject == null || tableService == null)
            return;

        if (tableService.CornerPocketDiameterM <= 0f)
            return;

        markerObject.transform.localScale = Vector3.one * tableService.CornerPocketDiameterM;
    }

    private Vector3 BuildFirstAnchorSpawnPoint()
    {
        Vector3 forward = GetPlayerPlanarForward();
        Vector3 right = GetPlayerPlanarRight(forward);

        Vector3 world = playerHead.position
                        + forward * spawnForwardDistanceM
                        + right * spawnSideOffsetM;

        world.y = tableService.TableY + anchorLiftAboveTableM;
        return world;
    }

    private Vector3 BuildSecondAnchorApproximateSpawnPoint(Vector3 bottomRightWorldPosition)
    {
        Vector3 forward = GetPlayerPlanarForward();
        Vector3 right = GetPlayerPlanarRight(forward);

        Vector3 world = bottomRightWorldPosition
                        - right * tableService.TableSize.x
                        + forward * tableService.TableSize.y;

        world.y = tableService.TableY + anchorLiftAboveTableM;
        return world;
    }

    private Vector3 BuildThirdBottomLeftAnchorApproximateSpawnPoint(Vector3 topLeftWorldPosition, Vector3 bottomRightWorldPosition)
    {
        Vector3 flattenedTopLeft = FlattenToTablePlane(topLeftWorldPosition, tableService.TableY);
        Vector3 flattenedBottomRight = FlattenToTablePlane(bottomRightWorldPosition, tableService.TableY);

        if (TryBuildPocketLayoutCandidatesFromDiagonalCorners(
                flattenedTopLeft,
                flattenedBottomRight,
                tableService.TableSize.x,
                tableService.TableSize.y,
                out Vector3[] candidateA,
                out Vector3[] candidateB))
        {
            Vector3 playerPlanarPosition = FlattenToTablePlane(playerHead.position, tableService.TableY);

            float candidateABottomScore = GetNearSideScore(playerPlanarPosition, candidateA[4], candidateA[5]);
            float candidateATopScore = GetNearSideScore(playerPlanarPosition, candidateA[0], candidateA[1]);

            float candidateBBottomScore = GetNearSideScore(playerPlanarPosition, candidateB[4], candidateB[5]);
            float candidateBTopScore = GetNearSideScore(playerPlanarPosition, candidateB[0], candidateB[1]);

            bool candidateAHasNearBottomRail = candidateABottomScore <= candidateATopScore;
            bool candidateBHasNearBottomRail = candidateBBottomScore <= candidateBTopScore;

            if (candidateAHasNearBottomRail && !candidateBHasNearBottomRail)
                return LiftAnchorSpawn(candidateA[4]);

            if (candidateBHasNearBottomRail && !candidateAHasNearBottomRail)
                return LiftAnchorSpawn(candidateB[4]);

            return LiftAnchorSpawn(candidateABottomScore <= candidateBBottomScore ? candidateA[4] : candidateB[4]);
        }

        Vector3 forward = GetPlayerPlanarForward();
        Vector3 right = GetPlayerPlanarRight(forward);

        Vector3 fallback = bottomRightWorldPosition - right * tableService.TableSize.x;
        fallback.y = tableService.TableY + anchorLiftAboveTableM;
        return fallback;
    }

    private Vector3 LiftAnchorSpawn(Vector3 worldPosition)
    {
        worldPosition.y = tableService.TableY + anchorLiftAboveTableM;
        return worldPosition;
    }

    private Vector3 GetPlayerPlanarForward()
    {
        Vector3 forward = Vector3.ProjectOnPlane(playerHead.forward, Vector3.up).normalized;
        return forward.sqrMagnitude <= 0.000001f ? Vector3.forward : forward;
    }

    private static Vector3 GetPlayerPlanarRight(Vector3 planarForward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, planarForward).normalized;
        return right.sqrMagnitude <= 0.000001f ? Vector3.right : right;
    }

    private void SetAnchorEditingEnabled(bool isEnabled)
    {
        for (int i = 0; i < _anchors.Length; i++)
        {
            GameObject anchor = _anchors[i];
            if (anchor == null)
                continue;

            XZOnlyConstraint constraint = anchor.GetComponent<XZOnlyConstraint>();
            if (constraint == null)
                continue;

            constraint.GrabbableEnabled = isEnabled;
        }
    }

    private void DestroyAnchorMarkers()
    {
        for (int i = 0; i < _anchors.Length; i++)
        {
            if (_anchors[i] != null)
                Destroy(_anchors[i]);

            _anchors[i] = null;
        }
    }

    private bool TryGetAnchorPosition(int anchorIndex, out Vector3 position)
    {
        if (_anchors[anchorIndex] != null)
        {
            position = _anchors[anchorIndex].transform.position;
            return true;
        }

        position = default;
        return false;
    }

    private static Vector3 FlattenToTablePlane(Vector3 position, float y) =>
        new(position.x, y, position.z);

    private static float GetNearSideScore(Vector3 playerPlanarPosition, Vector3 railPocketA, Vector3 railPocketB)
    {
        Vector3 railCenter = 0.5f * (railPocketA + railPocketB);
        railCenter.y = playerPlanarPosition.y;
        return (railCenter - playerPlanarPosition).sqrMagnitude;
    }

    private static bool TryBuildPocketLayoutCandidatesFromDiagonalCorners(
        Vector3 topLeft,
        Vector3 bottomRight,
        float tableLength,
        float tableWidth,
        out Vector3[] candidateA,
        out Vector3[] candidateB)
    {
        candidateA = null;
        candidateB = null;

        if (tableLength <= 0f || tableWidth <= 0f)
            return false;

        Vector3 diagonal = bottomRight - topLeft;
        diagonal.y = 0f;

        if (diagonal.sqrMagnitude <= 0.000001f)
            return false;

        Vector3 center = (topLeft + bottomRight) * 0.5f;
        Vector3 diagonalDir = diagonal.normalized;
        float diagonalToLongAxisAngleRad = Mathf.Atan2(tableWidth, tableLength);

        if (!TryBuildCandidatePocketLayout(center, diagonalDir, tableLength, tableWidth, diagonalToLongAxisAngleRad, out candidateA))
            return false;

        if (!TryBuildCandidatePocketLayout(center, diagonalDir, tableLength, tableWidth, -diagonalToLongAxisAngleRad, out candidateB))
            return false;

        return true;
    }

    private static bool TryBuildPocketLayoutFromThreeCorners(
        Vector3 topLeft,
        Vector3 bottomRight,
        Vector3 bottomLeft,
        float tableLength,
        float tableWidth,
        out Vector3[] questPocketWorldPositions)
    {
        questPocketWorldPositions = null;

        if (tableLength <= 0f || tableWidth <= 0f)
            return false;

        Vector3 rawLongAxis = bottomRight - bottomLeft;
        rawLongAxis.y = 0f;

        if (rawLongAxis.sqrMagnitude <= 0.000001f)
            return false;

        Vector3 longAxis = rawLongAxis.normalized;

        Vector3 rawShortAxis = topLeft - bottomLeft;
        rawShortAxis.y = 0f;

        Vector3 projectedShortAxis = Vector3.ProjectOnPlane(rawShortAxis, longAxis);
        if (projectedShortAxis.sqrMagnitude <= 0.000001f)
            return false;

        Vector3 shortAxis = projectedShortAxis.normalized;
        if (Vector3.Dot(shortAxis, rawShortAxis) < 0f)
            shortAxis = -shortAxis;

        Vector3 basisOrigin = (topLeft + bottomRight + bottomLeft) / 3f;

        float blU = ProjectToAxis(bottomLeft, basisOrigin, longAxis);
        float brU = ProjectToAxis(bottomRight, basisOrigin, longAxis);
        float tlU = ProjectToAxis(topLeft, basisOrigin, longAxis);

        float blV = ProjectToAxis(bottomLeft, basisOrigin, shortAxis);
        float brV = ProjectToAxis(bottomRight, basisOrigin, shortAxis);
        float tlV = ProjectToAxis(topLeft, basisOrigin, shortAxis);

        float leftU = 0.5f * (blU + tlU); // UPDATED: left rail uses the averaged table-local long coordinate of BL and TL
        float bottomV = 0.5f * (blV + brV); // UPDATED: bottom rail uses the averaged table-local short coordinate of BL and BR

        float rightU = leftU + tableLength;
        float topV = bottomV + tableWidth;
        float centerU = 0.5f * (leftU + rightU);

        Vector3 snappedTopLeft = ComposeFromBasis(basisOrigin, longAxis, shortAxis, leftU, topV);
        Vector3 snappedTopRight = ComposeFromBasis(basisOrigin, longAxis, shortAxis, rightU, topV);
        Vector3 snappedBottomLeft = ComposeFromBasis(basisOrigin, longAxis, shortAxis, leftU, bottomV);
        Vector3 snappedBottomRight = ComposeFromBasis(basisOrigin, longAxis, shortAxis, rightU, bottomV);
        Vector3 snappedBottomMiddle = ComposeFromBasis(basisOrigin, longAxis, shortAxis, centerU, bottomV);
        Vector3 snappedTopMiddle = ComposeFromBasis(basisOrigin, longAxis, shortAxis, centerU, topV);

        questPocketWorldPositions = new[]
        {
            snappedTopLeft,       // TL
            snappedTopRight,      // TR
            snappedBottomMiddle,  // ML in current TableService order (bottom-middle)
            snappedTopMiddle,     // MR in current TableService order (top-middle)
            snappedBottomLeft,    // BL
            snappedBottomRight    // BR
        };

        return true;
    }

    private static float ProjectToAxis(Vector3 worldPosition, Vector3 basisOrigin, Vector3 axis) =>
        Vector3.Dot(worldPosition - basisOrigin, axis);

    private static Vector3 ComposeFromBasis(Vector3 basisOrigin, Vector3 longAxis, Vector3 shortAxis, float u, float v)
    {
        Vector3 world = basisOrigin + longAxis * u + shortAxis * v;
        world.y = basisOrigin.y;
        return world;
    }

    private static bool TryBuildCandidatePocketLayout(
        Vector3 center,
        Vector3 diagonalDir,
        float tableLength,
        float tableWidth,
        float signedAngleRad,
        out Vector3[] candidatePocketWorldPositions)
    {
        candidatePocketWorldPositions = null;

        Vector3 longAxis = RotateAroundY(diagonalDir, signedAngleRad).normalized;
        if (longAxis.sqrMagnitude <= 0.000001f)
            return false;

        Vector3 shortAxis = Vector3.Cross(Vector3.up, longAxis).normalized;
        if (shortAxis.sqrMagnitude <= 0.000001f)
            return false;

        float halfLength = tableLength * 0.5f;
        float halfWidth = tableWidth * 0.5f;

        Vector3 pocketTopLeft = center - longAxis * halfLength + shortAxis * halfWidth;
        Vector3 pocketTopRight = center + longAxis * halfLength + shortAxis * halfWidth;
        Vector3 pocketBottomLeft = center - longAxis * halfLength - shortAxis * halfWidth;
        Vector3 pocketBottomRight = center + longAxis * halfLength - shortAxis * halfWidth;
        Vector3 pocketTopMiddle = center + shortAxis * halfWidth;
        Vector3 pocketBottomMiddle = center - shortAxis * halfWidth;

        candidatePocketWorldPositions = new[]
        {
            pocketTopLeft,
            pocketTopRight,
            pocketBottomMiddle,
            pocketTopMiddle,
            pocketBottomLeft,
            pocketBottomRight
        };

        return true;
    }

    private static Vector3 RotateAroundY(Vector3 vector, float radians)
    {
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);

        float x = vector.x * cos - vector.z * sin;
        float z = vector.x * sin + vector.z * cos;

        return new Vector3(x, 0f, z);
    }

    private string BuildStatusText()
    {
        return State switch
        {
            AlignmentState.Idle => "Idle",
            AlignmentState.PlacingBottomRightCorner => "Place the bottom-right anchor on the real corner pocket, then confirm it.",
            AlignmentState.BottomRightCornerPlaced => "Bottom-right anchor confirmed. Spawn the top-left anchor.",
            AlignmentState.PlacingTopLeftCorner => "Place the top-left anchor on the opposite corner pocket, then confirm it.",
            AlignmentState.TopLeftCornerPlaced => "Top-left anchor confirmed. Spawn the bottom-left anchor.",
            AlignmentState.PlacingBottomLeftCorner => "Place the bottom-left anchor on the remaining near-side corner pocket, then confirm it.",
            AlignmentState.BottomLeftCornerPlaced => "All three anchors are confirmed. Generate Quest pockets.",
            AlignmentState.EditingGeneratedPockets => _pythonFrameAligned
                                ? "Quest pockets aligned. Confirm pockets to freeze the frame."
                                : "Quest pockets generated. Adjust them, then align the Python frame.",
            AlignmentState.Confirmed => "Manual alignment confirmed.",
            _ => State.ToString(),
        };
    }

    private string BuildNextActionButtonText()
    {
        return State switch
        {
            AlignmentState.Idle => "Begin placing",
            AlignmentState.PlacingBottomRightCorner => "Confirm bottom right",
            AlignmentState.BottomRightCornerPlaced => "Spawn second top left",
            AlignmentState.PlacingTopLeftCorner => "Confirm top left",
            AlignmentState.TopLeftCornerPlaced => "Spawn third bottom left",
            AlignmentState.PlacingBottomLeftCorner => "Confirm bottom left",
            AlignmentState.BottomLeftCornerPlaced => "Confirm anchors",
            AlignmentState.EditingGeneratedPockets => _pythonFrameAligned ? "Confirm aligned pockets" : "Align Python frame with Quest frame",
            AlignmentState.Confirmed => "Alignment complete",
            _ => "Next",
        };
    }
}