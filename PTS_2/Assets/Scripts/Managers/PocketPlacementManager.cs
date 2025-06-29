using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class PocketPlacementManager : MonoBehaviour
{
    [Header("Marker Prefab")]
    public GameObject PocketMarkerPrefab;

    [Header("Placement Settings")]
    public Transform PlayerHead;
    public float placementDistance = 0.5f;

    private List<Transform> _placedMarkers = new();
    private int _markerIndex = 0;

    private List<Vector3> _pocketPositionsForTable = new();

    public readonly string[] PocketNames = new string[6]
    {
        "FL", "FR", "BL", "BR", "ML", "MR"
    };

    private void Start()
    {
        if (PlayerHead == null)
            PlayerHead = Camera.main.transform;
    }

    public void PlaceMarker()
    {
        if (_markerIndex >= 3)
        {
            Debug.Log("You can only place 3 markers.");
            return;
        }


        // TODO: Tell the where to place te first, secon and third marker.


        Vector3 placementPosition = PlayerHead.position + PlayerHead.forward * placementDistance;
        GameObject marker = Instantiate(PocketMarkerPrefab, placementPosition, Quaternion.identity);
        marker.name = $"PocketMarker_{_markerIndex}";
        _placedMarkers.Add(marker.transform);
        _markerIndex++;

        if (_markerIndex == 3)
        {
            ComputeRemainingPockets();
            Debug.Log("All markers placed. You can now save the positions.");
        }
    }

    private void ComputeRemainingPockets()
    {
        if (_placedMarkers.Count < 3)
        {
            Debug.Log("Not enough markers placed to compute remaining pockets. Needing 3.");
            return;
        }

        //TODO before commit: Add user instructions somewhere in the UI to show the user how to place the markers.

        ComputeIndividualPockets();
        SaveComputedTable(_pocketPositionsForTable);
        Debug.Log("Remaining pockets computed.");
    }

    private void ComputeIndividualPockets()
    {
        Vector3 frontLeft = _placedMarkers[0].position;
        Vector3 frontRight = _placedMarkers[1].position;
        Vector3 middleLeft = _placedMarkers[2].position;

        Vector3 tableRight = (frontRight - frontLeft).normalized;
        Vector3 tableForward = (middleLeft - frontLeft).normalized;

        float tableWidth = Vector3.Distance(frontLeft, frontRight);
        float halfTableLength = Vector3.Distance(frontLeft, middleLeft);

        Vector3 backLeft = frontLeft - tableForward * (2 * halfTableLength);
        Vector3 backRight = frontRight - tableForward * (2 * halfTableLength);

        Vector3 middleRight = middleLeft + tableRight * tableWidth;

        _pocketPositionsForTable = new List<Vector3>
        {
            frontLeft,  // FL
            frontRight, // FR
            backLeft,   // BL
            backRight,  // BR
            middleLeft, // ML
            middleRight // MR
        };
    }

    private void SaveComputedTable(List<Vector3> pockets)
    {
        Vector3 center = (pockets[0] + pockets[3]) / 2f;

        var serializedVectors = new List<Vector3Float>();
        foreach (var pocket in pockets)
        {
            serializedVectors.Add(new Vector3Float(pocket));
        }

        var savedTableData = new SaveTableData
        {
            PocketPositions = serializedVectors,
            Center = new Vector3Float(center)
        };

        AppSettings.Instance.SetAndSave(s => s.LastSavedTableData = savedTableData);

    }

    public void LoadPocketPositionsFromSettings()
    {
        var savedTableData = AppSettings.Instance.Settings.LastSavedTableData;
        if (savedTableData != null && savedTableData.PocketPositions.Count == 6)
        {
            _pocketPositionsForTable = savedTableData.GetPocketPositions();
            _markerIndex = 6; // All pockets are loaded
            Debug.Log("Loaded pocket positions from settings.");
        }
        else
        {
            _pocketPositionsForTable = InitZeroVectorList();
            Debug.LogWarning("No valid pocket positions found in settings.");
            _markerIndex = 0; // Reset marker index
        }
    }


    public List<Vector3> ReturnPocketPositions()
    {
        return _pocketPositionsForTable.Any(v => v != Vector3.zero) ?
         _pocketPositionsForTable :
         Enumerable.Empty<Vector3>().ToList();
    }

    public void SavePositions()
    {
        Debug.Log("Saving pocket marker positions...");
        for (int i = 0; i < _placedMarkers.Count; i++)
        {
            Debug.Log($"Pocket {i}: {_placedMarkers[i].position}");
        }
    }

    public void ClearMarkers()
    {
        foreach (var t in _placedMarkers)
        {
            if (t != null)
                Destroy(t.gameObject);
        }

        _placedMarkers.Clear();
        _markerIndex = 0;
    }

    private List<Vector3> InitZeroVectorList(int numberOfElements = 6)
    {
        var zeroVectorList = new List<Vector3>();
        for (int i = 0; i < numberOfElements; i++)
        {
            zeroVectorList.Add(Vector3.zero);
        }
        return zeroVectorList;
    }
}
