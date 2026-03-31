using System;
using UnityEngine;

[Serializable]
public class Vector2Float
{
    public float X { private set; get; }
    public float Y { private set; get; }

    public void SetX(float x) => X = x;
    public void SetY(float y) => Y = y;

    public Vector2Float(float x = 0f, float y = 0f)
    {
        X = x;
        Y = y;
    }
    public Vector2Float(UnityEngine.Vector2 vector2) : this(vector2.x, vector2.y) { }

    public static Vector2Float NormalizeAxis(Vector2Float axis)
    {
        if (axis == null)
            return default;

        float magnitude = GetMagnitude(axis);
        if (magnitude <= 0.0001f)
            return default;

        return new Vector2Float(axis.X / magnitude, axis.Y / magnitude);
    }

    public static float GetMagnitude(Vector2Float axis)
    {
        if (axis == null)
            return 0f;

        return Mathf.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y));
    }

    public static float Dot(float x, float y, Vector2Float axis)
    {
        if (axis == null)
            return 0f;

        return (x * axis.X) + (y * axis.Y);
    }
}
