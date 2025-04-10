using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class TableManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject PocketMarkerPrefab;
    public GameObject PlayableLineMarkerPrefab;
    public GameObject TableSelectionMarkerPrefab;

    [Header("Settings")]
    public float PocketYOffset = 0.01f;
    
    public bool ArePocketsComputed { get; private set; } = false;
    public Vector3[] PocketPositions { get; private set; } = new Vector3[6];

    private MRUKRoom _room;
    private List<MRUKAnchor> _tableAnchors = new();
    private Transform spawnedMarkerParent;

    private void Start()
    {
        // "SpawnedMarkers" hardcoded?
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

    private void SetupTableVisuals(Transform tableTransform, float length, float width)
    {
        ArePocketsComputed = true;

        float halfWidth = width / 2f;
        float halfLength = length / 2f;

        Vector3[] localPockets = new Vector3[6]
        {
            new(-halfWidth, 0, -halfLength), // Bottom Left
            new( halfWidth, 0, -halfLength), // Bottom Right
            new(-halfWidth, 0,  halfLength), // Top Left
            new( halfWidth, 0,  halfLength), // Top Right
            new(-halfWidth, 0,  0),          // Middle Left
            new( halfWidth, 0,  0),          // Middle Right
        };

        PocketPositions = new Vector3[6];

        for (int i = 0; i < 6; i++)
        {
            Vector3 worldPocket = tableTransform.TransformPoint(localPockets[i] + Vector3.up * PocketYOffset);
            PocketPositions[i] = worldPocket;
            Instantiate(PocketMarkerPrefab, worldPocket, Quaternion.identity, this.transform);
        }

        float offsetZ = -halfLength + (length / 3f);
        Vector3 lineLeft = tableTransform.TransformPoint(new Vector3(-halfWidth, PocketYOffset, offsetZ));
        Vector3 lineRight = tableTransform.TransformPoint(new Vector3(halfWidth, PocketYOffset, offsetZ));

        GameObject line = Instantiate(PlayableLineMarkerPrefab, this.transform);
        if (line.TryGetComponent<LineRenderer>(out var lr))
        {
            lr.positionCount = 2;
            lr.SetPosition(0, lineLeft);
            lr.SetPosition(1, lineRight);
        }
        else
        {
            Debug.LogWarning("PlayableLineMarkerPrefab requires LineRenderer component.");
        }
    }
}