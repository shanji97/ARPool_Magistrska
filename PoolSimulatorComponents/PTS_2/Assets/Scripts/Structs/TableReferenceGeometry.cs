using UnityEngine;

public struct TableReferenceGeometry
{
    public bool IsValid;
    public Vector3 Center;
    public Vector3 LongAxisLeftToRight;
    public Vector3 ShortAxisBottomToTop;
    public float TableLengthM;
    public float TableWidthM;

    public Vector3 QuarterLineCenter;
    public Vector3 QuarterLineStart;
    public Vector3 QuarterLineEnd;

    public Vector3 RackApex;
    public Vector3 RackBaseLeft;
    public Vector3 RackBaseRight;
}