using System;

[Serializable]
public class Vector3Float
{
    public float X { private set; get; }
    public float Y { private set; get; }
    public float Z { private set; get; }
    public void SetX(float x) => X = x;
    public void SetY(float y) => Y = y;
    public void SetZ(float z) => Z = z;

    public Vector3Float(float x = 0f, float y = 0f, float z = 0f)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3Float(float height) : this(0, height, 0) { }
    public Vector3Float(float x, float z) : this(x, 0, z) { }
    public Vector3Float(UnityEngine.Vector3 vector3) : this(vector3.x, vector3.y, vector3.z) { }
    public void SetHeight(float height) => Y = height;

    // XZ axis.
    public UnityEngine.Vector2 ToAxisXZ() => new(X, Z);
    public void ToVector3FromXZAxis(Vector2Float XZ)
    {
        X = XZ.X;
        Z = XZ.Y; // Unity's Y coordinate is the height coordinate.
    }

    public void ToVector3FromXZAxis(UnityEngine.Vector2 axisXZ)
    {
        X = axisXZ.x;
        Z = axisXZ.y;
    }

    public UnityEngine.Vector2 ToAxisXY() => new(X, Y);
    public void ToVector3FromXYAxis(Vector2Float XY)
    {
        X = XY.X;
        Y = XY.Y;
    }
    public void ToVector3FromXYAxis(UnityEngine.Vector2 axisXY)
    {
        X = axisXY.x;
        Y = axisXY.y;
    }

    public UnityEngine.Vector2 ToAxisZY() => new(Z, Y);
    public void ToVecto3FromZYAxis(UnityEngine.Vector2 axisZY)
    {
        Z = axisZY.x;
        Y = axisZY.y;
    }
    public UnityEngine.Vector3 ToUnityVector3() => new(X, Y, Z);
}