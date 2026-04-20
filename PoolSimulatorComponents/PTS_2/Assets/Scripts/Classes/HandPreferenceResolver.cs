public static class HandPreferenceResolver
{
    public static DominantHand GetDominantHand() =>
        AppSettings.Instance.Settings.DominantHand;

    public static bool IsLeftDominant() =>
        GetDominantHand() == DominantHand.Left;

    public static bool IsRightDominant() =>
        GetDominantHand() == DominantHand.Right;

    public static bool IsAmbidextrous() =>
        GetDominantHand() == DominantHand.Both;

    public static DominantHand GetCueHand() =>
        GetDominantHand();

    public static DominantHand GetGestureHand()
    {
        return GetDominantHand() switch
        {
            DominantHand.Left => DominantHand.Right,
            DominantHand.Right => DominantHand.Left,
            DominantHand.Both => DominantHand.Both,
            _ => DominantHand.Right
        };
    }

    public static bool IsCueHand(DominantHand hand) =>
        hand == GetCueHand() || GetCueHand() == DominantHand.Both;

    public static bool IsGestureHand(DominantHand hand) =>
        hand == GetGestureHand() || GetGestureHand() == DominantHand.Both;

    public static string GetCueHandDisplayName() =>
        GetCueHand().ToString();

    public static string GetGestureHandDisplayName() =>
        GetGestureHand().ToString();
}