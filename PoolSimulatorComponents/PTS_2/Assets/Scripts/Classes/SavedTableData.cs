
using System;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public class SaveTableData
{
    public List<Vector3Float> PocketPositions { get; set; } = new ();
    public Vector3Float Center { get; set; } = new Vector3Float(Vector3.zero);

    public List<Vector3> GetPocketPositions() =>
        PocketPositions.ConvertAll(v => v.FromVector3FloatToVector3());
}