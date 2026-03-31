using System;

[Serializable]
public struct DetectedCueStickSnapshot2D
{
    public Vector2Float LinePointXZ;
    public Vector2Float DirectionXZ;
    public Vector2Float HitPointXZ;
    public float Confidence;

    public DetectedCueStickSnapshot2D(
        Vector2Float linePointXZ,
        Vector2Float directionXZ,
        Vector2Float hitPointXZ,
        float confidence)
    {
        LinePointXZ = linePointXZ;
        DirectionXZ = directionXZ;
        HitPointXZ = hitPointXZ;
        Confidence = confidence;
    }

    public readonly bool IsValid() =>
        LinePointXZ != null &&
        DirectionXZ != null &&
        HitPointXZ != null &&
        IsFinite(LinePointXZ.X) &&
        IsFinite(LinePointXZ.Y) &&
        IsFinite(DirectionXZ.X) &&
        IsFinite(DirectionXZ.Y) &&
        IsFinite(HitPointXZ.X) &&
        IsFinite(HitPointXZ.Y) &&
        !IsNearZero(DirectionXZ.X, DirectionXZ.Y);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsNearZero(float x, float y) => ((x * x) + (y * y)) <= 0.0000001f;
}