using System;

[Serializable]
public struct DetectedBallSnapshot2D
{
    public BallType BallType;
    public byte RawIncomingId;
    public Vector2Float PositionXZ;
    public float Confidence;
    public Vector2Float VelocityXZ;

    public DetectedBallSnapshot2D(
        BallType ballType,
        byte rawIncomingId,
        Vector2Float positionXZ,
        float confidence,
        Vector2Float velocityXZ)
    {
        BallType = ballType;
        RawIncomingId = rawIncomingId;
        PositionXZ = positionXZ;
        Confidence = confidence;
        VelocityXZ = velocityXZ;
    }

    public readonly bool IsValid() =>
        PositionXZ != null &&
        !float.IsNaN(PositionXZ.X) &&
        !float.IsNaN(PositionXZ.Y) &&
        !float.IsInfinity(PositionXZ.X) &&
        !float.IsInfinity(PositionXZ.Y);
}