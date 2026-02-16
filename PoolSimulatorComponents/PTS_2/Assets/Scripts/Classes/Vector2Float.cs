using System;

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
}
