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
    private Vector3[] _lastMarkerPosition;
    public bool IsLockedToJitter { get; private set; } = false;
    public bool LockFinalized { get; private set; } = false;

    public float CameraHeightFromFloor { get; private set; } = -1f;
    public bool IsCameraFromFloorSet() => CameraHeightFromFloor > 0;

    public float BallDiameterM = -1f;
    public bool IsBallDiameterSet() => BallDiameterM > 0;

    public float BallCircumferenceM = -1f;
    public bool IsBallCircumferenceSet() => BallCircumferenceM > 0;

    public bool AreBallPropertiesSet() => IsBallDiameterSet() && IsBallCircumferenceSet();
    public bool ArePropertiesParsed() => Is2DTableSet() && IsTableHeightSet() && IsCameraFromFloorSet();

    public string[] QR_CODE_MARKER_VALUES =
    {
        "ARPOOL_MARKER_01",
        "ARPOOL_MARKER_02",
        "ARPOOL_MARKER_03",
        "ARPOOL_MARKER_04",
        "ARPOOL_MARKER_05",
        "ARPOOL_MARKER_06",
        "ARPOOL_MARKER_07",
        "ARPOOL_MARKER_08",
        "ARPOOL_MARKER_09",
        "ARPOOL_MARKER_10",
        "ARPOOL_MARKER_11",
        "ARPOOL_MARKER_12"
    };

    public const float QR_CODE_WHOLE_PAPER_SIZE_M = 0.019f;

    public bool AreDiamondsParsed() => DiamondCount == MAX_DIAMOND_COUNT;

    private bool VerboseDiamondLogs()
    {
#if UNITY_EDITOR
        return true;
#else
            return false;
#endif
    }

    private bool _enviromentSaved = false;

    public byte QR_MARKER_COUNT = 2;

    private const float movingTreshold = .0005f;

    private GameObject[] _markers;
    public readonly byte MAX_POCKET_COUNT = 6;
    public readonly byte MAX_DIAMOND_COUNT = 18;

    public const byte StripeCount = 7;
    public byte[] StripedBalls = new byte[] { 9, 10, 11, 12, 13, 14, 15 };

    public const byte SolidCount = 7;
    public byte[] SolidBalls = new byte[StripeCount] { 1, 2, 3, 4, 5, 6, 7 };

    public const byte MaxBallCount = SolidCount + StripeCount + 2;

    // 0 - 6 solids, 7 eight, 8 - 14 stripes, cue 15
    private List<Vector3Float> _balls = new(MaxBallCount);

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

        // UPDATED: make sure _balls is always safe to index (even before any ball messages arrive)
        EnsureBallBufferSize();
    }

    public void LateUpdate()
    {
        if (LockFinalized || !MaintainRectangleWhenLocked || _markers is null) return;

        // Detect which marker moved the most this frame.
        HandlePocketMarkers();
    }

    public void OnDestroy()
    {
        if (_tableStateEntries.Any())
            AppSettings.Instance.AddTableStateEntries(_tableStateEntries);
    }

    public void OnApplicationQuit()
    {
        if (_tableStateEntries.Any())
            AppSettings.Instance.AddTableStateEntries(_tableStateEntries);
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

            // MODIFIED: logical cache and visible marker stay identical.
            UpdatePocketPositionCache(i, worldPocketPosition);
            SetMarkerWorldPose(i, worldPocketPosition);
        }

        if (_lastMarkerPosition == null || _lastMarkerPosition.Length != MAX_POCKET_COUNT)
            _lastMarkerPosition = new Vector3[MAX_POCKET_COUNT];

        for (byte i = 0; i < MAX_POCKET_COUNT; i++)
        {
            _lastMarkerPosition[i] = _markers[i] != null
                ? _markers[i].transform.position
                : PocketPositions[i];
        }

        Debug.Log($"[TableService] Pockets placed at markerY={markerY:F4}, TableY={TableY:F4}, BallDiameterM={BallDiameterM:F4}");
    }

    public bool CanFinalizePocketPlacement() => ArePropertiesParsed()
            && HasAllPockets()
            && _markers != null
            && _markers.Length == MAX_POCKET_COUNT;

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

        Debug.Log("[TableService] Pocket placement finalized. Markers are frozen in place.");
    }

    public void ReEnablePocketEditing()
    {
        LockFinalized = false;
        IsLockedToJitter = false;

        ApplyLockStateToMarkers();

        Debug.Log("[TableService] Pocket editing re-enabled.");
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
        if (AppSettings.Instance.Settings.DeviceInformation == DeviceInformation.PrimaryQuest)
        {
            // Send to other Quest 3 devices.
        }



        if ((id <= (byte)BallType.Cue))
        {
            // Work directly on the ball array.
            // Handle Jitter - distinguish between this and the actuall movement. Probably both the unity script and the python script should do this? Also multiple
        }
        else
        {
            // Wtf should I do here?
            /*
             Idea:
             Accumulate 14 (we don't need it for cue and eightball) XY locations of balls. 
             Account for jitter and situation when two or more balls are next to one another.
             Determine whether they are solid or striped.
             For each ball, create a selection menu and make the user select the type (number).
             */
        }

        // Create a buffer for a fluid system of tracking each ball for multiple points.
    }
    public void ReapplyPockets(float tableY)
    {
        if (tableY <= 0f) return;

        if (BallDiameterM <= 0f)
        {
            if (!_warnedMissingBallSpec)
            {
                Debug.LogWarning("[TableService] ReapplyPockets skipped: BallDiameterM is not set yet.");
                _warnedMissingBallSpec = true;
            }
            return;
        }

        if (_markers == null || _markers.Length != MAX_POCKET_COUNT) return;
        if (PocketCount == 0) return;

        bool wasLocked = IsLockedToJitter;
        if (wasLocked) IsLockedToJitter = false;

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

        if (wasLocked) IsLockedToJitter = true;
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

        switch (moved)
        {
            case 0: leftX = TL.x; topZ = TL.z; break;
            case 1: rightX = TR.x; topZ = TR.z; break;
            case 4: leftX = BL.x; bottomZ = BL.z; break;
            case 5: rightX = BR.x; bottomZ = BR.z; break;
            case 2: bottomZ = ML.z; break;
            case 3: topZ = MR.z; break;
        }

        float centerX = 0.5f * (leftX + rightX);
        float markerY = GetPocketMarkerY(TableY);

        TL = new Vector3(leftX, markerY, topZ);
        TR = new Vector3(rightX, markerY, topZ);
        BL = new Vector3(leftX, markerY, bottomZ);
        BR = new Vector3(rightX, markerY, bottomZ);
        ML = new Vector3(centerX, markerY, bottomZ);
        MR = new Vector3(centerX, markerY, topZ);

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

    private float GetDefaultBallHeight(float tableY)
    {
        if (tableY <= 0f) return tableY;
        if (BallDiameterM <= 0f) return tableY;
        return tableY + (BallDiameterM * 0.5f);
    }

    private float GetPocketMarkerY(float tableY) => GetDefaultBallHeight(tableY) + SurfaceLift;


    private void SetMarkerWorldPose(byte index, Vector3 worldPosition)
    {
        if (_markers == null || index < 0 || index >= _markers.Length)
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

        // UPDATED: cache the exact final pose before disabling editing
        if (marker.TryGetComponent<XZOnlyConstraint>(out var constraint))
        {
            if (!editingEnabled)
            {
                constraint.SetConstrainedWorldPose(marker.transform.position, marker.transform.rotation);
            }

            constraint.GrabbableEnabled = editingEnabled;
        }

        // UPDATED: toggle all colliders on the marker and its children
        Collider[] colliders = marker.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var collider in colliders)
        {
            collider.enabled = editingEnabled;
        }

        Rigidbody[] rigidbodies = marker.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        foreach (var rb in rigidbodies)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = !editingEnabled;
            rb.detectCollisions = editingEnabled;
            rb.constraints = editingEnabled ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeAll;
        }

        // UPDATED: disable Meta/Oculus interaction scripts directly by type name
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
        _balls ??= new List<Vector3Float>(MaxBallCount);

        while (_balls.Count < MaxBallCount)
            _balls.Add(null);

        if (_balls.Count > MaxBallCount)
            _balls.RemoveRange(MaxBallCount, _balls.Count - MaxBallCount);
    }

    private void SetBallHeight()
    {
        EnsureBallBufferSize();

        if (TableY <= 0f || BallDiameterM <= 0f)
            return;

        float h = GetDefaultBallHeight(TableY);

        for (byte i = 0; i < MaxBallCount; i++)
        {
            if (_balls[i] == null)
                _balls[i] = new Vector3Float(h);
            else
                _balls[i].SetHeight(h);
        }
    }
    private static short MToRoundedMm(float valueM) // UPDATED: save meters back into *_mm JSON fields correctly
    {
        int mm = Mathf.RoundToInt(valueM * 1000f);
        mm = Mathf.Clamp(mm, short.MinValue, short.MaxValue);
        return (short)mm;
    }

}