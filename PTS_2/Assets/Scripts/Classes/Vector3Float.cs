using System;
using UnityEngine;


[Serializable]
public class Vector3Float
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    public Vector3Float (float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3Float (Vector3 vector3)
    {
        X = vector3.x;
        Y = vector3.y;
        Z = vector3.z;
    }

    public Vector3 FromVector3FloatToVector3() => new(X, Y, Z);
}