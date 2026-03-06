using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TableService : MonoBehaviour
{
    public static TableService Instance { get; private set; }

    [Header("Visuals")]
    public GameObject PocketMarkerPrefab;
    public Transform MarkersParent;
    public int PocketCount { get; private set; } = 0;
    public bool HasAllPockets() => PocketCount == MaxPocketCount;

    [Tooltip("Offset above table surface to avoid z-fighting")]
    public float SurfaceLift = 0.01f;

    [Tooltip("Scale of default primitive sphere if prefab is null")]
    public float DefaultSphereScale = 0.03f;

    [Header("State (read-only)")]
    public readonly Vector3[] PocketPositions = new Vector3[6];  // TL,TR,ML,MR,BL,BR

    public float TableY { get; private set; } = -1f;
    public bool IsTableHeightSet() => TableY > 0;

    public Vector2 TableSize = new(-1, -1);
    public bool Is2DTableSet() => TableSize.x > 0 && TableSize.y > 0;

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
    public bool ArePropertiesParsed() => Is2DTableSet() && IsTableHeightSet() && IsCameraFromFloorSet() && AreBallPropertiesSet();

    private bool _enviromentSaved = false;

    public byte MarkerCount = 2;

    private const float movingTreshold = .0005f;

    private GameObject[] _markers;
    public readonly byte MaxPocketCount = 6;

    public const byte StripeCount = 7;
    public byte[] StripedBalls = new byte[] { 9, 10, 11, 12, 13, 14, 15 };

    public const byte SolidCount = 7;
    public byte[] SolidBalls = new byte[StripeCount] { 1, 2, 3, 4, 5, 6, 7 };

    public const byte MaxBallCount = SolidCount + StripeCount + 2;

    // 0 - 6 solids, 7 eight, 8 - 14 stripes, cue 15
    private List<Vector3Float> _balls = new(MaxBallCount);

    private List<TableStateEntry> _tableStateEntries = null;

    // UPDATED: one-time warning guard
    private bool _warnedMissingBallSpec = false;

    public void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _tableStateEntries = new();

        // UPDATED: make sure _balls is always safe to index (even before any ball messages arrive)
        EnsureBallBufferSize();
    }

    public void LateUpdate()
    {
        if (!LockFinalized || !MaintainRectangleWhenLocked || _markers is null) return;

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

    public void IncrementPocketCount()
    {
        if (PocketCount == 6) return;
        PocketCount++;
    }

    /// <summary>
    /// Sets all six pockets in XZ (meters) and a table Y (meters).
    /// Order: TL, TR, ML, MR, BL, BR. X->Unity X, Z->Unity Z.
    /// Ignored if locked.
    /// </summary>
    public void SetPocketsXZ((float x, float z)[] pocketXZ, float tableY)
    {
        if (IsLockedToJitter) return;
        if (pocketXZ?.Length != MaxPocketCount) return;

        float ballCenterY = GetDefaultBallHeight(tableY);
        TableY = tableY;

        EnsureMarkers();

        for (byte i = 0; i < MaxPocketCount; i++)
        {
            PocketPositions[i] = new Vector3(pocketXZ[i].x, ballCenterY, pocketXZ[i].z);
        }
        // Wait until we have read the 2(or 4 markers with known positions and set all the pockets, compute the marker position, get the offes
        //_markers[i].transform.position = new Vector3(pocketXZ[i].x, ballCenterY + SurfaceLift, pocketXZ[i].z);

        Debug.Log("Pockets placed.");
    }

    //public void DetectMarker()
    public void SetMarkersBasedOnQRDetections()
    {
        // Safeguard the pocket positions
        if (PocketPositions?.Any(pp => pp == null) != false || PocketPositions.Length != 6)
        {
            Debug.Log("The pockets have not yet been calculated.");
            return;
        }

        EnsureMarkers();
        float ballCenterY = GetDefaultBallHeight(TableY);

        for (byte i = 0; i < MaxPocketCount; i++)
            _markers[i].transform.position = new Vector3(PocketPositions[i].x, ballCenterY + SurfaceLift, PocketPositions[i].z);

    }

    public void SetPocketsXZ((float x, float y)[] pocketXZ) => SetPocketsXZ(pocketXZ, TableY);

    public void EnsureMarkers()
    {
        if (_markers?.Length == MaxPocketCount) return;
        _markers = new GameObject[6];

        for (byte i = 0; i < MaxPocketCount; i++)
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

    public void SetBallDiameter(float ballDiameter)
    {
        BallDiameterM = ballDiameter > 0f ? ballDiameter : BallDiameterM;
        // UPDATED: if we already have table height, we can now safely apply ball height
        SetBallHeight();
    }

    public void SetBallCircumference(float ballCircumference) => BallCircumferenceM = ballCircumference > 0f ? ballCircumference : BallCircumferenceM;
    public void SetTableLenght(float length) => SetTable(length, TableSize.y, TableY);
    public void SetTableWidth(float width) => SetTable(TableSize.x, width, TableY);
    public void SetTableHeight(float height) => SetTable(TableSize.x, TableSize.y, height);
    public void SetTable(Vector2 widthAndLength, float height) => SetTable(widthAndLength.x, widthAndLength.y, height);
    public void SetTable(Vector3 tableDimensions) => SetTable(tableDimensions.x, tableDimensions.z, tableDimensions.y);
    public void SetTable(Vector3Float tableDimensions) => SetTable(tableDimensions.X, tableDimensions.Z, tableDimensions.Y);
    public void SetTable(EnvironmentInfo env) => SetTable(env.Table.Length, env.Table.Width, env.Table.Height);

    public void SetTable(float length, float width, float newTableY)
    {
        if (IsLockedToJitter) return;

        TableSize = new Vector2(length > 0 ? length : TableSize.x, width > 0 ? width : TableSize.y);

        // If height changes, pockets might need lifting (but ReapplyPockets is now safe-guarded)
        if (newTableY > 0 && newTableY != TableY)
            ReapplyPockets(newTableY);

        TableY = newTableY;

        // UPDATED: safe even if _balls wasn't populated yet
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
        SetLocked(true);
        LockFinalized = true;
        _lastMarkerPosition = null;
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

            // UPDATED: these were swapped before
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

        // if ball diameter isn't known, pocket markers shouldn't be lifted to "ball center"
        if (BallDiameterM <= 0f)
        {
            if (!_warnedMissingBallSpec)
            {
                Debug.LogWarning("[TableService] ReapplyPockets skipped: BallDiameterM is not set yet.");
                _warnedMissingBallSpec = true;
            }
            return;
        }

        // If we never created markers / pockets, do nothing.
        if (_markers == null || _markers.Length != MaxPocketCount) return;
        if (PocketCount == 0) return;

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

        TL = new Vector3(leftX, TableY, topZ);
        TR = new Vector3(rightX, TableY, topZ);
        BL = new Vector3(leftX, TableY, bottomZ);
        BR = new Vector3(rightX, TableY, bottomZ);
        ML = new Vector3(centerX, TableY, bottomZ);
        MR = new Vector3(centerX, TableY, topZ);

        _markers[0].transform.position = new Vector3(TL.x, TableY + SurfaceLift, TL.z);
        _markers[1].transform.position = new Vector3(TR.x, TableY + SurfaceLift, TR.z);
        _markers[2].transform.position = new Vector3(ML.x, TableY + SurfaceLift, ML.z);
        _markers[3].transform.position = new Vector3(MR.x, TableY + SurfaceLift, MR.z);
        _markers[4].transform.position = new Vector3(BL.x, TableY + SurfaceLift, BL.z);
        _markers[5].transform.position = new Vector3(BR.x, TableY + SurfaceLift, BR.z);

        for (sbyte i = 0; i < 6; i++)
            _lastMarkerPosition[i] = _markers[i].transform.position;
    }

    private float GetDefaultBallHeight(float tableY)
    {
        if (tableY <= 0f) return tableY;
        if (BallDiameterM <= 0f) return tableY;
        return tableY + (BallDiameterM * 0.5f);
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

    private void EnsureBallBufferSize()
    {
        if (_balls == null)
            _balls = new List<Vector3Float>(MaxBallCount);

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
}