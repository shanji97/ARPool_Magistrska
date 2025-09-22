using UnityEngine;

[RequireComponent(typeof(Transform))]
public class XZOnlyConstraint : MonoBehaviour
{
    [Tooltip("Whether this marker can currently be grabbed by the user.")]
    public bool GrabbableEnabled = true;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private bool _isInitialized = false;

    public void Initialize(Quaternion worldRotation)
    {
        _initialPosition = transform.position;
        _initialRotation = worldRotation;
        _isInitialized = true;
    }

    public void Initialize() => Initialize(transform.rotation);

    private void LateUpdate()
    {
        if (!_isInitialized || !GrabbableEnabled || !this.isActiveAndEnabled)
            return;

        // Lock Y height
        Vector3 pos = transform.position;
        pos.y = _initialPosition.y;
        
        // Lock rotation to preserved
        transform.SetPositionAndRotation(pos, _initialRotation);
    }
}