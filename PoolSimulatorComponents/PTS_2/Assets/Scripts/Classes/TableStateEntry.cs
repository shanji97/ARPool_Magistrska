using System;
using System.Collections.Generic;

[Serializable]
public class TableStateEntry
{
    public TableStateEntry(global::GameMode mode, DeviceInformation deviceInformation, byte? excerciseId = null)
    {
        GameMode = mode == global::GameMode.InMenu
            ? global::GameMode.SoloGame
            : mode;

        if (GameMode == global::GameMode.LessonsMode)
        {
            if (excerciseId == null)
                throw new ArgumentNullException(nameof(excerciseId), "Exercise id cannot be null in LessonsMode.");

            ExcreciseData = new ExerciseData(excerciseId.Value);
        }

        const int maxRequiredBallsOnTable = 16;
        int requiredBallCapacity = maxRequiredBallsOnTable;

        requiredBallCapacity = GameMode switch
        {
            global::GameMode.SoloGame or global::GameMode.OnlineCoop or global::GameMode.LocalCoop => maxRequiredBallsOnTable,
            global::GameMode.LessonsMode => ExerciseData.GetMaxNumberOfNeededBallForExercise(
                excerciseId ?? throw new ArgumentNullException(nameof(excerciseId))),
            _ => maxRequiredBallsOnTable,
        };

        BallInfo = new List<Ball>(requiredBallCapacity);
        DataFromDevice = deviceInformation;
    }

    public DateTime EntryDate { get; } = DateTime.Now;

    public global::GameMode GameMode { get; } = global::GameMode.SoloGame;

    public ExerciseData ExcreciseData { get; private set; } = null;

    public List<Ball> BallInfo { get; private set; } = null;

    public DeviceInformation DataFromDevice { get; private set; } = DeviceInformation.PrimaryQuest;

    public void ResetAllBallOverrides()
    {
        if (BallInfo == null)
            return;

        for (int i = 0; i < BallInfo.Count; i++)
            BallInfo[i]?.ResetUserOverrides();
    }
}