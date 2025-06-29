
using UnityEngine;

public class SpawnMenuInFrontOfPlayer : MonoBehaviour
{
    public Transform PlayerHead;

    public bool SpawnInFrontOfPlayer;

    [SerializeField]
    private Vector3 _offsetFromPlayerHead;

    void Awake()
    {
        SpawnUiInFrontOfHead();
    }

    private void SpawnUiInFrontOfHead()
    {
        if(PlayerHead == null)
        {
            PlayerHead = Camera.main.transform;
        }

        var spawnPosition = PlayerHead.position + 
            PlayerHead.forward * _offsetFromPlayerHead.z +
            PlayerHead.right * _offsetFromPlayerHead.x +
            PlayerHead.up * _offsetFromPlayerHead.y;

        transform.position = spawnPosition;
        transform.LookAt(PlayerHead);
        transform.forward = - transform.forward;
    }
}
