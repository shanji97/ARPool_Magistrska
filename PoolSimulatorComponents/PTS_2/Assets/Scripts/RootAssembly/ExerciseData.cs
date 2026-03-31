using System;
using System.Collections.Generic;

[Serializable]
public class ExerciseData
{
    private const byte SmallestExerciseId = 1;
    private const byte LargestExerciseId = 10;

    public ExerciseData(uint? excerciseId, byte numberOfAttempts = 10)
    {
        if (excerciseId == null)
            throw new ArgumentNullException(nameof(excerciseId), "Exercise ID cannot be null.");

        if (excerciseId < SmallestExerciseId || excerciseId > LargestExerciseId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(excerciseId),
                $"Exercise id must be between {SmallestExerciseId} and {LargestExerciseId}.");
        }

        ExerciseId = excerciseId;
        Attemps = new List<Attempt>(numberOfAttempts);
    }

    public uint? ExerciseId { get; } = null;

    public readonly List<Attempt> Attemps;

    public static byte GetMaxNumberOfNeededBallForExercise(byte exerciseId)
    {
        const byte minRequiredBallsOnTable = 2;
        return minRequiredBallsOnTable;
    }
}