using System;

[Serializable]
public sealed class IncomingDetectedBallRecord
{
    public IncomingDetectedBallRecord(
        BallType ballType,
        byte rawIncomingId,
        Vector2Float positionXZ,
        float confidence,
        Vector2Float velocityXZ = null)
    {
        BallType = ballType;
        RawIncomingId = rawIncomingId;
        PositionXZ = positionXZ;
        Confidence = confidence;
        VelocityXZ = velocityXZ;
    }

    public BallType BallType { get; }

    public byte RawIncomingId { get; }

    public Vector2Float PositionXZ { get; }

    public float Confidence { get; }

    public Vector2Float VelocityXZ { get; }
}