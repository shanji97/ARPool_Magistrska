using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class TableManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject PocketMarkerPrefab;
    public GameObject PlayableLineMarkerPrefab;
    public GameObject TableSelectionMarkerPrefab;

    [Header("Settings")]
    public float PocketYOffset = 0.01f;
    public Vector3[] PocketPositions { get; private set; } = new Vector3[6];

    [Header("Variables")]
    public readonly string[] PocketNames = { "FL", "FR", "BL", "BR", "ML", "MR" };

    private MRUKRoom _room;

    private List<MRUKAnchor> _tableAnchors = new();
    private List<Vector3> _initialMarkerPositions = new();
    private List<bool> _arePocketsComputedForTable = new();

    public List<bool> AreAllPocketsComputedForTables()
    {
        if (!_arePocketsComputedForTable.Any())
            return default;
        else
            return _arePocketsComputedForTable;
    }


    private Transform spawnedMarkerParent;
    public TableManager Instance { get; private set; }

    private readonly List<string> _spawnedMarkerNames = new();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(this);
    }

    private void Start()
    {
        spawnedMarkerParent = new GameObject("SpawnedMarkers").transform;
        StartCoroutine(WaitForRoomCreation());
    }

    private IEnumerator WaitForRoomCreation()
    {
        while (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null || !MRUK.Instance.GetCurrentRoom().Anchors.Any())
        {
            yield return null;
        }

        _room = MRUK.Instance.GetCurrentRoom();
        DetectAndMarkTables();
    }

    private void DetectAndMarkTables()
    {
        _arePocketsComputedForTable = new();

        _tableAnchors = _room.Anchors
            .Where(a => a.AnchorLabels.Contains(SemanticLabel.TABLE))
            .ToList();

        if (_tableAnchors.Count == 0)
        {
            Debug.LogWarning("No table anchors found.");
            return;
        }

        int anchorNumber = 0;
        foreach (var anchor in _tableAnchors)
        {
            Transform anchorTransform = anchor.transform;

            // Place marker slightly above the table
            Vector3 topOfTable = anchorTransform.position + anchorTransform.up * PocketYOffset;

            GameObject marker = Instantiate(TableSelectionMarkerPrefab, topOfTable, Quaternion.identity);
            marker.name = $"SelectTable_{anchorNumber}";

            _initialMarkerPositions.Add(marker.transform.position);
            _spawnedMarkerNames.Add(marker.name);
            _arePocketsComputedForTable.Add(false);

            FaceMarkerTowaardsCamera(marker.transform);
            marker.transform.SetParent(spawnedMarkerParent, true);

            anchorNumber++;
        }
    }

    private void FaceMarkerTowaardsCamera(Transform markerTransform)
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("Main camera not found — canvas won't be rotated.");
            return;
        }

        Vector3 toCamera = Camera.main.transform.position - markerTransform.position;
        toCamera.y = 0f;

        var clippingDistance = 0.001f; // Not sure if this is the right name to use. Rename.
        markerTransform.rotation = toCamera.sqrMagnitude > clippingDistance ?
            Quaternion.LookRotation(toCamera.normalized, Vector3.up) :
            markerTransform.rotation;
    }

    public void SetupTableVisuals(Transform markerTransform)
    {
        var indexOfTable = _spawnedMarkerNames.IndexOf(markerTransform.name);
        if (indexOfTable == -1)
        {
            Debug.LogWarning($"Table with name {markerTransform.name} not found in spawned markers.");
            return;
        }

        if (_arePocketsComputedForTable[indexOfTable])
        {
            Debug.LogWarning($"Pockets for table {markerTransform.name} have already been computed.");
            return;
        }

        var anchor = _tableAnchors[indexOfTable];
        if (anchor == null)
        {
            Debug.LogWarning($"Anchor for table {markerTransform.name} not found.");
            return;
        }

        var originalMarkerPosition = _initialMarkerPositions[indexOfTable];
        if (originalMarkerPosition == null || originalMarkerPosition == Vector3.zero)
        {
            Debug.LogWarning($"Original marker position for table {markerTransform.name} is not set or zero.");
            return;
        }

        _arePocketsComputedForTable[indexOfTable] = true;

        Transform anchorTransform = anchor.transform;

        Vector3 tableRight = anchorTransform.right.normalized;
        Vector3 tableUp = anchorTransform.up.normalized;
        Vector3 tableForward = Vector3.Cross(tableRight, tableUp).normalized;

        Bounds bounds = anchor.VolumeBounds.Value;

        // Get half dimensions
        float halfLength = bounds.extents.z;
        float halfWidth = bounds.extents.x;

        // Adjust if table is rotated (longer side might not always be length)
        if (bounds.size.z > bounds.size.x)
        {
            halfLength = bounds.extents.z;
            halfWidth = bounds.extents.x;
        }
        else
        {
            halfLength = bounds.extents.x;
            halfWidth = bounds.extents.z;
        }

        float y = originalMarkerPosition.y + PocketYOffset;
        Vector3 tableCenter = anchorTransform.position;

        // Calculate all pocket positions
        // Middle pockets
        Vector3 midLeft = tableCenter - tableRight * halfWidth;
        midLeft.y = y;
        Vector3 midRight = tableCenter + tableRight * halfWidth;
        midRight.y = y;

        // Front and back positions (along the length)
        Vector3 frontCenter = tableCenter + tableForward * halfLength;
        Vector3 backCenter = tableCenter - tableForward * halfLength;

        // Corner pockets
        Vector3 frontLeft = frontCenter - tableRight * halfWidth;
        frontLeft.y = y;
        Vector3 frontRight = frontCenter + tableRight * halfWidth;
        frontRight.y = y;
        Vector3 backLeft = backCenter - tableRight * halfWidth;
        backLeft.y = y;
        Vector3 backRight = backCenter + tableRight * halfWidth;
        backRight.y = y;

        // Instantiate and store all pocket positions
        GameObject[] pockets = new GameObject[6];
        for (int i = 0; i < 6; i++)
        {
            Vector3 position = i switch
            {
                0 => frontLeft,    // FL
                1 => frontRight,   // FR
                2 => backLeft,     // BL
                3 => backRight,    // BR
                4 => midLeft,      // ML
                5 => midRight,     // MR
                _ => Vector3.zero
            };

            pockets[i] = Instantiate(PocketMarkerPrefab, position, Quaternion.identity);
            pockets[i].name = $"Pocket_{PocketNames[i]}_{indexOfTable}";
            PocketPositions[i] = position;
            pockets[i].transform.SetParent(spawnedMarkerParent, true);
        }
    }
}
