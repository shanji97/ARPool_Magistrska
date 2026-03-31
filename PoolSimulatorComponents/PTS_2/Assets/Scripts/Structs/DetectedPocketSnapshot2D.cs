using System;

[Serializable]
public struct DetectedPocketSnapshot2D
{
    public byte PocketIndex;
    public Vector2Float PositionXZ;

    public DetectedPocketSnapshot2D(byte pocketIndex, Vector2Float positionXZ)
    {
        PocketIndex = pocketIndex;
        PositionXZ = positionXZ;
    }

    public readonly bool IsValid() =>
        PositionXZ != null &&
        !float.IsNaN(PositionXZ.X) &&
        !float.IsNaN(PositionXZ.Y) &&
        !float.IsInfinity(PositionXZ.X) &&
        !float.IsInfinity(PositionXZ.Y);
}