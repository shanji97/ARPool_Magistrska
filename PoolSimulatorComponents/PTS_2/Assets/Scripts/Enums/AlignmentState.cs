public enum AlignmentState : byte
{
    Idle = 0,

    PlacingBottomRightCorner = 10,
    BottomRightCornerPlaced = 11,

    PlacingTopLeftCorner = 20,
    TopLeftCornerPlaced = 21,

    PlacingBottomLeftCorner = 30,
    BottomLeftCornerPlaced = 31,

    EditingGeneratedPockets = 40,
    Confirmed = 50
}