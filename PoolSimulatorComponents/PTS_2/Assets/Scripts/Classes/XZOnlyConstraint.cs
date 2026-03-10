using UnityEngine;

[RequireComponent(typeof(Transform))]
public class XZOnlyConstraint : MonoBehaviour
{
    [Tooltip("Whether this marker can currently be grabbed by the user.")]
    public bool GrabbableEnabled = true;

    private Vector3 _lockedWorldPosition;          // UPDATED: full locked position for final freeze
    private float _lockedWorldY;
    private Quaternion _lockedWorldRotation;
    private bool _isInitialized = false;
    private bool _previousGrabbableEnabled = true; // UPDATED: detect editable -> locked transition

    public void Initialize()
    {
        Initialize(transform.position, transform.rotation);
    }

    public void Initialize(Quaternion worldRotation)
    {
        Initialize(transform.position, worldRotation);
    }

    public void Initialize(Vector3 worldPosition, Quaternion worldRotation)
    {
        _lockedWorldPosition = worldPosition;      // UPDATED: cache full pose
        _lockedWorldY = worldPosition.y;
        _lockedWorldRotation = worldRotation;
        _isInitialized = true;
        _previousGrabbableEnabled = GrabbableEnabled;
    }

    public void SetConstrainedWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);

        _lockedWorldPosition = worldPosition;      // UPDATED: keep full frozen pose in sync
        _lockedWorldY = worldPosition.y;
        _lockedWorldRotation = worldRotation;
        _isInitialized = true;
        _previousGrabbableEnabled = GrabbableEnabled;
    }

    private void LateUpdate()
    {
        if (!_isInitialized || !isActiveAndEnabled)
            return;

        // UPDATED: when switching from editable to locked, capture the final valid pose once
        if (_previousGrabbableEnabled && !GrabbableEnabled)
        {
            Vector3 frozen = transform.position;
            frozen.y = _lockedWorldY; // keep marker at the table height
            _lockedWorldPosition = frozen;
        }

        if (GrabbableEnabled)
        {
            Vector3 pos = transform.position;
            pos.y = _lockedWorldY;

            // Editable mode: only X/Z can change, Y and rotation stay fixed.
            transform.SetPositionAndRotation(pos, _lockedWorldRotation);

            // UPDATED: keep tracking the most recent valid editable pose
            _lockedWorldPosition = pos;
        }
        else
        {
            // Locked mode: freeze the entire world pose in place every frame.
            transform.SetPositionAndRotation(_lockedWorldPosition, _lockedWorldRotation);
        }

        _previousGrabbableEnabled = GrabbableEnabled;
    }
}