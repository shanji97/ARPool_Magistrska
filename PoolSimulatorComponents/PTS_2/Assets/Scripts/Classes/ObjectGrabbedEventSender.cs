using Oculus.Interaction;
using UnityEngine;

public class ObjectGrabbedEventSender : GrabFreeTransformer, ITransformer
{
    public delegate void ObjectGrabbed(GameObject source);
    public event ObjectGrabbed OnObjectGrabbed;
    public delegate void ObjectMoved(GameObject source);
    public event ObjectMoved OnObjectMoved;
    public delegate void ObjectReleased(GameObject source);
    public event ObjectReleased OnObjectReleased;

    public new void Initialize(IGrabbable grabbable)
    { 
        base.Initialize(grabbable);
    }

    public new void BeginTransform()
    {
        base.BeginTransform();
        OnObjectGrabbed?.Invoke(gameObject);
    }
    
    public new void UpdateTransform()
    {
        base.UpdateTransform();
        OnObjectMoved?.Invoke(gameObject);
    }

    public new void EndTransform()
    {
        OnObjectReleased?.Invoke(gameObject);
    }
}
