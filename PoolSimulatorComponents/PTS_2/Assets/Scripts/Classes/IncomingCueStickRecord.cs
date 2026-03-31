using System;

[Serializable]

public class IncomingCueStickRecord
{
    public Vector2Float LinePointXZ;
    public Vector2Float DirectionXZ;
    public Vector2Float HitPointXZ;
    public float Confidence;

    public IncomingCueStickRecord(Vector2Float linePointXZ, Vector2Float directionXZ, Vector2Float hitPointXZ, float confidence = 0f)
    {
        LinePointXZ = linePointXZ;
        DirectionXZ = directionXZ;
        HitPointXZ = hitPointXZ;
        Confidence = confidence;
    }

    public Vector3Float GetLinePoint(float y = 0f) => new(LinePointXZ.X, y, LinePointXZ.Y);
    public Vector3Float GetHitPointXZ(float y = 0f) => new(HitPointXZ.X, y, HitPointXZ.Y);
    public Vector3Float GetDirection(float y = 0f) => new(DirectionXZ.X, y, DirectionXZ.Y);
}