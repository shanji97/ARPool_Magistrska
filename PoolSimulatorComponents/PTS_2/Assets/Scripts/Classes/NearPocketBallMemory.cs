using System;

[Serializable]
public class NearPocketBallMemory
{
    public BallType BallType;
    public byte RawIncomingId;
    public Vector2Float PositionXZ;
    public byte PocketIndex = byte.MaxValue;
    public PocketZoneBallState State = PocketZoneBallState.Visible;
    public float DistanceToPocketM = -1f;
    public float Confidence = -1f;
    public float LastSeenTime = -1f;
    public bool IsSpecialBall = false;

    public void UpdateState(
        BallType ballType,
        byte rawIncomingId,
        Vector2Float positionXZ,
        byte pocketIndex,
        float distanceToPocketM,
        float confidence,
        PocketZoneBallState state,
        float lastSeenTime,
        bool isSpecialBall)
    {
        BallType = ballType;
        RawIncomingId = rawIncomingId;
        PositionXZ = positionXZ;
        PocketIndex = pocketIndex;
        DistanceToPocketM = distanceToPocketM;
        Confidence = confidence;
        State = state;
        LastSeenTime = lastSeenTime;
        IsSpecialBall = isSpecialBall;
    }
}