public class IncomingCueStickRecord
{
    public Vector2Float LinePointXZ;
    public Vector2Float DirectionXZ;
    public Vector2Float HitPointXZ;
    public float Confidence;

    public IncomingCueStickRecord(Vector2Float linePointXZ, Vector2Float direction, Vector2Float hitpoint, float confidence = 0f)
    {
        LinePointXZ = linePointXZ;
        DirectionXZ = direction;
        HitPointXZ = hitpoint;
        Confidence = confidence;
    }

    public Vector3Float GetLinePoint(float y = 0) => new Vector3Float(LinePointXZ.X, y, LinePointXZ.Y);
    public Vector3Float GetHitPointXZ(float y = 0) => new Vector3Float(HitPointXZ.X, y, HitPointXZ.Y);
    public Vector3Float GetDirection(float y = 0) => new Vector3Float(HitPointXZ.X, y, HitPointXZ.Y);
}