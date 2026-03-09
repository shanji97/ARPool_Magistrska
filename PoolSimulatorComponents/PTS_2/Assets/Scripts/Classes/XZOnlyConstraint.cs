using UnityEngine;

[RequireComponent(typeof(Transform))]
public class XZOnlyConstraint : MonoBehaviour
{
    [Tooltip("Whether this marker can currently be grabbed by the user.")]
    public bool GrabbableEnabled = true;

    private float _lockedWorldY;
    private Quaternion _lockedWorldRotation;
    private bool _isInitialized = false;

    public void Initialize()
    {
        Initialize(transform.position, transform.rotation);
    }

    public void Initialize(Quaternion worldRotation)
    {
        Initialize(transform.position, worldRotation);
    }

    // MODIFIED: explicit world-space initialization so TableService can refresh
    // the locked Y after programmatic marker placement.
    public void Initialize(Vector3 worldPosition, Quaternion worldRotation)
    {
        _lockedWorldY = worldPosition.y;
        _lockedWorldRotation = worldRotation;
        _isInitialized = true;
    }

    // MODIFIED: this is the important helper used by TableService after every
    // programmatic move, so the marker does not snap back to an outdated Y.
    public void SetConstrainedWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        _lockedWorldY = worldPosition.y;
        _lockedWorldRotation = worldRotation;
        _isInitialized = true;
    }

    private void LateUpdate()
    {
        if (!_isInitialized || !GrabbableEnabled || !isActiveAndEnabled)
            return;

        Vector3 pos = transform.position;
        pos.y = _lockedWorldY;

        // Keep the current X/Z, but preserve the locked world Y and rotation.
        transform.SetPositionAndRotation(pos, _lockedWorldRotation);
    }
}