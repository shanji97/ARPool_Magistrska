// Attach to: GameplaySystems/BallPositionCorrectionController in PoolSetup scene
using System.Collections.Generic;
using UnityEngine;

public class BallPositionCorrectionController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private ManualBallOverrideService manualBallOverrideServiceOverride;
    [SerializeField] private TableService tableServiceOverride;

    [Header("Handle")]
    [SerializeField] private GameObject positionCorrectionHandlePrefab;
    [SerializeField] private Transform positionCorrectionHandlesParent;
    [SerializeField] private float positionCorrectionHandleLiftM = 0.03f;
    [SerializeField] private float positionHandleMovementEpsilonM = 0.001f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private static readonly HashSet<string> MarkerInteractionComponentTypeNames = new()
    {
        "Grabbable",
        "HandGrabInteractable",
        "DistanceHandGrabInteractable",
        "RayInteractable",
        "PokeInteractable"
    };

    private ManualBallOverrideService _manualBallOverrideService;
    private TableService _tableService;
    private GameObject _handleInstance;
    private Vector3 _lastAppliedHandleWorldPosition;
    private bool _handleInitialized;

    private void Awake() => ResolveDependencies();

    private void OnEnable()
    {
        ResolveDependencies();

        if (_manualBallOverrideService != null)
        {
            _manualBallOverrideService.SelectedBallChanged += HandleSelectedBallChanged;
            _manualBallOverrideService.SelectedBallPositionCorrectionModeChanged += HandleSelectedBallPositionCorrectionModeChanged;
        }
    }

    private void OnDisable()
    {
        if (_manualBallOverrideService != null)
        {
            _manualBallOverrideService.SelectedBallChanged -= HandleSelectedBallChanged;
            _manualBallOverrideService.SelectedBallPositionCorrectionModeChanged -= HandleSelectedBallPositionCorrectionModeChanged;
        }

        SetHandleActive(false);
        _handleInitialized = false;
    }

    private void LateUpdate()
    {
        ResolveDependencies();

        if (!ShouldShowHandle())
        {
            SetHandleActive(false);
            _handleInitialized = false;
            return;
        }

        EnsureHandleInstance();
        SyncHandleToSelectedBallCorrection();
    }

    public void BeginSelectedBallPositionCorrection()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
            return;

        if (_manualBallOverrideService.BeginSelectedPositionCorrection(out string statusMessage))
        {
            _handleInitialized = false;

            if (verboseLogs)
                Debug.Log($"[BallPositionCorrectionController] {statusMessage}");
        }
        else if (verboseLogs)
        {
            Debug.LogWarning($"[BallPositionCorrectionController] {statusMessage}");
        }
    }

    public void ConfirmSelectedBallPositionCorrection()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
            return;

        _manualBallOverrideService.ConfirmSelectedPositionCorrection();
        SetHandleActive(false);
        _handleInitialized = false;
    }

    public void CancelSelectedBallPositionCorrection()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
            return;

        _manualBallOverrideService.CancelSelectedPositionCorrection();
        SetHandleActive(false);
        _handleInitialized = false;
    }

    public void ReleaseSelectedBallPositionOverride()
    {
        ResolveDependencies();

        if (_manualBallOverrideService == null)
            return;

        _manualBallOverrideService.ReleaseSelectedPositionOverride();
        SetHandleActive(false);
        _handleInitialized = false;
    }

    private void ResolveDependencies()
    {
        _manualBallOverrideService = manualBallOverrideServiceOverride != null
            ? manualBallOverrideServiceOverride
            : ManualBallOverrideService.Instance;

        _tableService = tableServiceOverride != null
            ? tableServiceOverride
            : TableService.Instance;
    }

    private bool ShouldShowHandle() =>
        _manualBallOverrideService != null &&
        _tableService != null &&
        _manualBallOverrideService.SelectedBall != null &&
        _manualBallOverrideService.IsSelectedBallInPositionCorrectionMode;

    private void SyncHandleToSelectedBallCorrection()
    {
        if (_handleInstance == null || _manualBallOverrideService?.SelectedBall == null || _tableService == null)
            return;

        if (!_tableService.TryGetBallRestingWorldY(out float ballRestingY))
            return;

        if (!_handleInitialized)
        {
            if (!TryGetSelectedBallWorldPosition(ballRestingY, out Vector3 selectedBallWorldPosition))
                return;

            Vector3 initialHandleWorldPosition = selectedBallWorldPosition + (Vector3.up * positionCorrectionHandleLiftM);

            SetHandleWorldPose(initialHandleWorldPosition);
            _lastAppliedHandleWorldPosition = initialHandleWorldPosition;
            _handleInitialized = true;
        }

        Vector3 requestedHandleWorldPosition = _handleInstance.transform.position;
        Vector3 requestedBallWorldPosition = requestedHandleWorldPosition - (Vector3.up * positionCorrectionHandleLiftM);

        if (!_tableService.TryClampBallCenterToPlayableSurface(
                requestedBallWorldPosition,
                out Vector3 clampedBallWorldPosition,
                out _))
        {
            return;
        }

        Vector3 clampedHandleWorldPosition = clampedBallWorldPosition + (Vector3.up * positionCorrectionHandleLiftM);
        SetHandleWorldPose(clampedHandleWorldPosition);

        if ((clampedHandleWorldPosition - _lastAppliedHandleWorldPosition).sqrMagnitude < positionHandleMovementEpsilonM * positionHandleMovementEpsilonM)
            return;

        _ = _manualBallOverrideService.TryMoveSelectedBallToWorldPosition(clampedBallWorldPosition, out _);
        _lastAppliedHandleWorldPosition = clampedHandleWorldPosition;
    }

    private bool TryGetSelectedBallWorldPosition(float ballRestingY, out Vector3 selectedBallWorldPosition)
    {
        selectedBallWorldPosition = Vector3.zero;

        Ball selectedBall = _manualBallOverrideService?.SelectedBall;
        Vector2Float effectivePosition = selectedBall?.GetEffectivePosition();

        if (effectivePosition == null)
            return false;

        selectedBallWorldPosition = new Vector3(effectivePosition.X, ballRestingY, effectivePosition.Y);
        return true;
    }

    private void EnsureHandleInstance()
    {
        if (_handleInstance != null)
        {
            SetHandleActive(true);
            ApplyHandleEditState(_handleInstance, true);
            return;
        }

        Transform parent = positionCorrectionHandlesParent != null
            ? positionCorrectionHandlesParent
            : transform;

        if (positionCorrectionHandlePrefab != null)
        {
            _handleInstance = Instantiate(positionCorrectionHandlePrefab, parent);
        }
        else
        {
            _handleInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _handleInstance.transform.SetParent(parent, false);
            _handleInstance.transform.localScale = Vector3.one * 0.04f;

            Rigidbody rigidbody = _handleInstance.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = _handleInstance.AddComponent<Rigidbody>();

            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = true;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        }

        _handleInstance.name = "BallPositionCorrectionHandle";

        if (!_handleInstance.TryGetComponent<XZOnlyConstraint>(out XZOnlyConstraint constraint))
            constraint = _handleInstance.AddComponent<XZOnlyConstraint>();

        constraint.Initialize(_handleInstance.transform.position, _handleInstance.transform.rotation);

        ApplyHandleEditState(_handleInstance, true);
        SetHandleActive(true);
    }

    private void ApplyHandleEditState(GameObject handle, bool editingEnabled)
    {
        if (handle == null)
            return;

        if (handle.TryGetComponent<XZOnlyConstraint>(out XZOnlyConstraint constraint))
        {
            constraint.SetConstrainedWorldPose(handle.transform.position, handle.transform.rotation);
            constraint.GrabbableEnabled = editingEnabled;
        }

        Collider[] colliders = handle.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;
            collider.enabled = editingEnabled;
        }

        Rigidbody[] rigidbodies = handle.GetComponentsInChildren<Rigidbody>(includeInactive: true);
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

        Behaviour[] behaviours = handle.GetComponentsInChildren<Behaviour>(includeInactive: true);
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;
            if (MarkerInteractionComponentTypeNames.Contains(typeName))
                behaviour.enabled = editingEnabled;
        }
    }

    private void SetHandleWorldPose(Vector3 worldPosition)
    {
        if (_handleInstance == null)
            return;

        if (_handleInstance.TryGetComponent<XZOnlyConstraint>(out XZOnlyConstraint constraint))
        {
            constraint.SetConstrainedWorldPose(worldPosition, Quaternion.identity);
        }
        else
        {
            _handleInstance.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        }
    }

    private void SetHandleActive(bool isActive)
    {
        if (_handleInstance != null && _handleInstance.activeSelf != isActive)
            _handleInstance.SetActive(isActive);
    }

    private void HandleSelectedBallChanged(BallOverrideSelectable _)
    {
        _handleInitialized = false;
    }

    private void HandleSelectedBallPositionCorrectionModeChanged(BallOverrideSelectable _, bool isActive)
    {
        if (!isActive)
        {
            SetHandleActive(false);
            _handleInitialized = false;
            return;
        }

        _handleInitialized = false;
    }
}