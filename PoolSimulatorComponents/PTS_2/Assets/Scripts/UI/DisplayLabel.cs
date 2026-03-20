using UnityEngine;
using Meta.XR.MRUtilityKit;

public class DisplayLabel : MonoBehaviour
{

    public Transform rayStartPoint;
    public float rayLength = 5f;

    public MRUKAnchor.SceneLabels labelsFilter;
    public TMPro.TextMeshPro debugText;

    public void Update()
    {
        var ray = new Ray(rayStartPoint.position, rayStartPoint.forward);
        var room = MRUK.Instance.GetCurrentRoom();

        if (room != null)
        {
            var hasHit = room.Raycast(ray, rayLength, LabelFilter.FromEnum(labelsFilter), out var hitInfo, out MRUKAnchor anchorHit);
            if (hasHit)
            {
                    var hitPoint = hitInfo.point;
                    var hitNormal = hitInfo.normal;
                    var label = anchorHit.AnchorLabels[0];

                    debugText.transform.SetPositionAndRotation(hitPoint, Quaternion.LookRotation(-hitNormal));
                    debugText.text = $"Anchor: {label}";
            }
        }
    }
}