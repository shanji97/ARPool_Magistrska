using System;
using UnityEngine;

[Serializable]
public struct TableFrame2D
{
    public Vector2Float CenterXZ;
    public Vector2Float LongAxis;
    public float LenghtM;
    public float WidthM;

    public readonly float LengthM => LenghtM;

    public readonly Vector2Float ShortAxis
    {
        get
        {
            Vector2Float normalizedLongAxis = Vector2Float.NormalizeAxis(LongAxis);
            return new Vector2Float(-normalizedLongAxis.Y, normalizedLongAxis.X);
        }
    }

    public readonly bool IsValid()
    {
        return CenterXZ != null &&
               LongAxis != null &&
               IsFinite(CenterXZ.X) &&
               IsFinite(CenterXZ.Y) &&
               IsFinite(LongAxis.X) &&
               IsFinite(LongAxis.Y) &&
               Vector2Float.GetMagnitude(LongAxis) > 0.0001f &&
               LenghtM > 0.0001f &&
               WidthM > 0.0001f;
    }

    public readonly Vector2Float WorldToLocal(Vector2Float worldXZ)
    {
        if (!IsValid() || worldXZ == null)
            return default;

        Vector2Float normalizedLongAxis = Vector2Float.NormalizeAxis(LongAxis);
        Vector2Float normalizedShortAxis = ShortAxis;

        float deltaX = worldXZ.X - CenterXZ.X;
        float deltaZ = worldXZ.Y - CenterXZ.Y;

        float localLong = Vector2Float.Dot(deltaX, deltaZ, normalizedLongAxis);
        float localShort = Vector2Float.Dot(deltaX, deltaZ, normalizedShortAxis);

        return new Vector2Float(localLong, localShort);
    }

    public readonly Vector2Float LocalToWorld(Vector2Float localXZ)
    {
        if (!IsValid() || localXZ == null)
            return default;

        Vector2Float normalizedLongAxis = Vector2Float.NormalizeAxis(LongAxis);
        Vector2Float normalizedShortAxis = ShortAxis;

        float worldX =
            CenterXZ.X +
            (normalizedLongAxis.X * localXZ.X) +
            (normalizedShortAxis.X * localXZ.Y);

        float worldZ =
            CenterXZ.Y +
            (normalizedLongAxis.Y * localXZ.X) +
            (normalizedShortAxis.Y * localXZ.Y);

        return new Vector2Float(worldX, worldZ);
    }

    public readonly Vector2Float LocalMetersToNormalized(Vector2Float localXZ)
    {
        if (!IsValid() || localXZ == null)
            return default;

        float u = (localXZ.X / LenghtM) + 0.5f;
        float v = (localXZ.Y / WidthM) + 0.5f;

        return new Vector2Float(u, v);
    }

    public readonly Vector2Float NormalizedToLocalMeters(Vector2Float uv)
    {
        if (!IsValid() || uv == null)
            return default;

        float localLong = (uv.X - 0.5f) * LenghtM;
        float localShort = (uv.Y - 0.5f) * WidthM;

        return new Vector2Float(localLong, localShort);
    }

    public readonly Vector2Float ClampNormalized(Vector2Float uv)
    {
        if (uv == null)
            return default;

        return new Vector2Float(
            Mathf.Clamp01(uv.X),
            Mathf.Clamp01(uv.Y));
    }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}