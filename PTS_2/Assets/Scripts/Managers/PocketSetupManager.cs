using Oculus.Interaction;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PocketSetupManager : MonoBehaviour
{
    [Header("Marker Placement Settings")]
    [Tooltip("Prefab for the pocket marker (must have collider, rigidbody, Grabbable, HandGrabInteractable, etc.).")]
    public GameObject markerPrefab;
    public GameObject pocketMarkerRoot;

    //OVR Controls
    [Tooltip("Reference to left hand OVRHand component (for gesture detection).")]
    public OVRHand leftHand;
    [Tooltip("Reference to right hand OVRHand component (for gesture detection).")]
    public OVRHand rightHand;
    [Tooltip("Event sources for left and right source.")]
    public OVRMicrogestureEventSource LeftHandEventSource;
    public OVRMicrogestureEventSource RightHandEventSource;

    [Tooltip("Reference to the hand visual components.")]
    public Oculus.Interaction.HandVisual leftHandVisual;
    public Oculus.Interaction.HandVisual rightHandVisual;

    [Tooltip("Controller button to place a marker (fallback). e.g., Button.One = A (Right) or X (Left).")]
    public OVRInput.Button placeMarkerButton = OVRInput.Button.One;
    [Tooltip("Controller button to undo last marker (fallback). e.g., Button.Two = B (Right) or Y (Left).")]
    public OVRInput.Button undoMarkerButton = OVRInput.Button.Two;

    [Header("UI")]
    [Tooltip("Floating instruction text displayed to the user")]
    public TextMeshProUGUI instructionTextUI;

    [Header("Privates")]
    [Tooltip("Floating instruction text displayed to the user")]
    [SerializeField]
    private string _instructionText = string.Empty;
    [SerializeField]
    private sbyte _totalPockedNeeded = 3;

    private float _yCoordinateValueForMarkers = 0;

    private readonly string[] _pocketNames =
    {
        PocketName.BottomLeftCorner.ToString(),
        PocketName.BottomRightCorner.ToString(),
        PocketName.RightMiddle.ToString(),
        PocketName.TopLeftCorner.ToString(),
        PocketName.TopRightCorner.ToString(),
        PocketName.LeftMiddle.ToString(),
    };

    private List<GameObject> _placedMarkers = new(6);
    private sbyte _markersPlacedCount = 0;
    private bool _groupModeActivate = false;
    private bool _allPocketsCalculated = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private void Awake()
    {
        if (instructionTextUI != null)
        {
            instructionTextUI.text = _instructionText;
            UpdateInstructionUI();
        }
        else
        {
            Debug.LogWarning("Instruction text UI refence not set.");
        }
    }
    void Start()
    {
        if (LeftHandEventSource == null || RightHandEventSource == null)
        {
            Debug.LogError($"No {nameof(OVRMicrogestureEventSource)} component attached to this gameobject.");
        }
        else
        {
            LeftHandEventSource.GestureRecognizedEvent.AddListener(gesture => OnMicrogestureRecognized(leftHand, gesture));
            RightHandEventSource.GestureRecognizedEvent.AddListener(gesture => OnMicrogestureRecognized(rightHand, gesture));
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (_groupModeActivate) return;
        //TODO
    }

    void OnApplicationQuit()
    {
        LeftHandEventSource.GestureRecognizedEvent.RemoveAllListeners();
        RightHandEventSource.GestureRecognizedEvent.RemoveAllListeners();
    }

    private void OnDestroy()
    {
        LeftHandEventSource.GestureRecognizedEvent.RemoveAllListeners();
        RightHandEventSource.GestureRecognizedEvent.RemoveAllListeners();
    }

    public void OnMicrogestureRecognized(OVRHand hand, OVRHand.MicrogestureType gestureType)
    {
        Debug.Log($"Microgesture event: {gestureType} from hand {hand.name}.");

        switch (gestureType)
        {
            case OVRHand.MicrogestureType.ThumbTap:
                PlacePocketMarker(hand);
                break;
            case OVRHand.MicrogestureType.SwipeRight:
                FinalizePlacements();
                break;
            case OVRHand.MicrogestureType.SwipeLeft:
            case OVRHand.MicrogestureType.SwipeForward:
            case OVRHand.MicrogestureType.SwipeBackward:
            default:
                Debug.Log("Gesture currently not supported.");
                break;
        }

        UpdateInstructionUI();
    }

    private void FinalizePlacements()
    {
        if (_allPocketsCalculated)
        {
            _instructionText = "All pockets have been placed at their respected positions. Nothing to do here.";
            Debug.Log(_instructionText);
            UpdateInstructionUI();

            return;
        }

        if (_markersPlacedCount < _totalPockedNeeded)
        {
            Debug.LogWarning("Cannot finalize: not all required pocket markers are placed yet.");
            return;
        }

        Vector3 bottomLeft = _placedMarkers[0].transform.position;
        Vector3 middleLeft = _placedMarkers[1].transform.position;
        Vector3 bottomRight = _placedMarkers[2].transform.position;

        //Top left
        Vector3 bottomLeftToLeftMiddle = middleLeft - bottomLeft;
        Vector3 topLeft = bottomLeft + 2 * bottomLeftToLeftMiddle;

        //Right middle
        Vector3 bottomLeftToBottomRight = bottomRight - bottomLeft;
        Vector3 middleRight = middleLeft + bottomLeftToBottomRight;

        //Top Right
        Vector3 topRight = topLeft + bottomLeftToBottomRight;

        topLeft.y = _yCoordinateValueForMarkers;
        middleRight.y = _yCoordinateValueForMarkers;
        topRight.y = _yCoordinateValueForMarkers;

        GameObject topLeftMarker = Instantiate(markerPrefab, topLeft, Quaternion.identity);
        topLeftMarker.name = _pocketNames[3];
        GameObject middleRightMarker = Instantiate(markerPrefab, middleRight, Quaternion.identity);
        middleRightMarker.name = _pocketNames[4];
        GameObject topRightMarker = Instantiate(markerPrefab, topRight, Quaternion.identity);
        topRightMarker.name = _pocketNames[5];

        if (pocketMarkerRoot != null)
        {
            topLeftMarker.transform.SetParent(pocketMarkerRoot.transform, true);
            middleRightMarker.transform.SetParent(pocketMarkerRoot.transform, true);
            topRightMarker.transform.SetParent(pocketMarkerRoot.transform, true);
        }

        _markersPlacedCount += 3;


        _instructionText = "All 6 pockets positioned!";
        Debug.Log("All pockets placed and finalized.");
        _allPocketsCalculated = true;
        UpdateInstructionUI();
    }

    private void PlacePocketMarker(OVRHand hand)
    {
        if (_markersPlacedCount == _totalPockedNeeded)
        {
            _instructionText = "All of the required pockets have been instantiated. You can grab them and reposition them, before swiping right.";
            UpdateInstructionUI();
            return;
        }

        //if (_groupModeActivate) return;
        if (pocketMarkerRoot == null)
        {
            Debug.LogError("No pocket marker root has been instantiated or has been deleted in runtime, so no (furher) pockets can be placed.");
            return;
        }

        //if (_markersPlacedCount >= _totalPockedNeeded || _groupModeActivate || pocketMarkerRoot == null)
        //return;

        var palmPosition = GetPalmWorldPosition(hand);

        // For the first pocket, store the Y-level
        if (_markersPlacedCount == 0)
        {
            _yCoordinateValueForMarkers = palmPosition.y;
            Debug.Log($"PalmPosition {palmPosition.y}");
            Debug.DrawRay(palmPosition, Vector3.up * 0.1f, Color.green, 2f);
        }

        // Override Y to always match the first pocket’s Y
        palmPosition.y = _yCoordinateValueForMarkers;

        // Instantiate marker at (X,Z) of your hand + fixed Y
        GameObject marker = Instantiate(markerPrefab, palmPosition, Quaternion.identity);
        marker.name = _pocketNames[_markersPlacedCount];
        marker.transform.SetParent(pocketMarkerRoot.transform, worldPositionStays: true);

        _placedMarkers.Add(marker);
        _markersPlacedCount++;

        // Feedback message
        sbyte remaining = (sbyte)(_totalPockedNeeded - _markersPlacedCount);
        _instructionText = remaining > 0
            ? $"<b>{marker.name}</b> placed! {remaining} more to go…"
            : $"<b>{marker.name}</b> placed! Swipe right to confirm.";

        UpdateInstructionUI();
    }

    private Vector3 GetPalmWorldPosition(OVRHand hand)
    {
        HandVisual handVisual = (hand == leftHand) ? leftHandVisual : rightHandVisual;

        if (handVisual == null)
        {
            Debug.LogWarning("Hand visual component not assigned.");
            return hand.transform.position;
        }

        return handVisual.GetTransformByHandJointId(Oculus.Interaction.Input.HandJointId.HandPalm).position;
    }

    private void UpdateInstructionUI()
    {
        if (instructionTextUI != null)
        {
            instructionTextUI.text = _instructionText;
            _instructionText = string.Empty;
        }
        else
        {
            Debug.LogWarning("Instruction text UI not assigned.");
        }
    }

    public void UndoLastMarker()
    {
        if (_markersPlacedCount == 0) return;


        _markersPlacedCount--;
        _markersPlacedCount = _markersPlacedCount < 0 ? (sbyte)0 : _markersPlacedCount;
    }
}
