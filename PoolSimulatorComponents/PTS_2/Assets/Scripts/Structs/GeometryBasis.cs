using UnityEngine;

public struct GeometryBasis
{
    public float MarkingY;
    public float BallDiameterM;

    public Vector3 LeftShortRailCenter;
    public Vector3 RightShortRailCenter;
    public Vector3 BottomLongRailCenter;
    public Vector3 TopLongRailCenter;

    public Vector3 Center;
    public Vector3 LongAxisLeftToRight;
    public Vector3 ShortAxisBottomToTop;

    public float TableLengthM;
    public float TableWidthM;

    public float QuarterLineHalfWidthM;
    public Vector3 RackDepthDirection;

    public Vector3 DeterministicQuarterLineCenter;
    public Vector3 DeterministicRackApex;
}