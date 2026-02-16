[System.Flags]
public enum GameMode
{
    InMenu = 1,
    SoloGame = 2,
    LocalCoop = 4,
    OnlineCoop = 8,
    LessonsMode = 16,

    // Combinations
    MixedCoop = LocalCoop | OnlineCoop
}
