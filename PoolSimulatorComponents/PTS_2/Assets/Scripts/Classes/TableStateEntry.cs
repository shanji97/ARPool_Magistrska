using System;
using System.Collections.Generic;

[Serializable]
public class TableStateEntry
{
    public TableStateEntry(GameMode mode, DeviceInformation deviceInformation, byte? excerciseId = null)
    {
        GameMode = mode == GameMode.InMenu ? GameMode : mode;
        if (GameMode == GameMode.LessonsMode && excerciseId != null)
            ExcreciseData = new(excerciseId.Value);

        const byte MAX_REQUIRED_BALLS_ON_TABLE = 16;
        switch (GameMode)
        {
            case GameMode.SoloGame:
            case GameMode.OnlineCoop:
            case GameMode.LocalCoop:
                BallInfo = new(MAX_REQUIRED_BALLS_ON_TABLE);
                break;
            case GameMode.LessonsMode:
                if (excerciseId == null)
                    throw new ArgumentNullException("Excercise id cannot be null.");

                BallInfo = new(ExerciseData.GetMaxNumberOfNeededBallForExercise(excerciseId.Value));
                break;
            default:
                BallInfo = new(MAX_REQUIRED_BALLS_ON_TABLE);
                break;
        }

        DataFromDevice = deviceInformation;

    }
    public DateTime EntryDate { get; } = DateTime.Now;

    public GameMode GameMode { get; } = GameMode.SoloGame;

    public ExerciseData ExcreciseData = null;

    public List<Ball> BallInfo { get; } = null;

    public DeviceInformation DataFromDevice { get; } = DeviceInformation.PrimaryQuest;

    // Compare entries from different Quests.
}

[Serializable]
public class Ball
{
    public BallType BallType;
    public byte BallId { private set; get; }
    public Vector2Float DetectedPosition;
    public Vector2Float CorrectedPosition { get; private set; } = null;

    public bool UserModifiedPosition { get; private set; } = false;

    public UserOverrides UserOverrides { get; private set; }

    public bool IsPositionUserOverriden() => (UserOverrides & UserOverrides.UserModifiedPosition) == UserOverrides.UserModifiedPosition;
    public bool IsTypeUserOverriden() => (UserOverrides & UserOverrides.UserModifiedType) == UserOverrides.UserModifiedPosition;

    public void AssignBallId(byte ballId)
    {
        BallId = Math.Clamp(ballId, (byte)BallType.Solid, (byte)BallType.Cue);
    }

    public void ModifyPosition(Vector2Float newPosition)
    {
        if (UserModifiedPosition)
            return;

        CorrectedPosition = newPosition;
        UserOverrides |= UserOverrides.UserModifiedPosition;
        //Set the graphics to new position.



    }

    public void MoveToOriginalPosition()
    {
        // Set to original position


        /*
         * Option 1: Convert both positions to Vector3
                     Set the visual to LERP to OG position
                     Set the corrected position to null.
                    (separation of concerns)

        1
        */
        /* Option 2: Despawn the ball at the corrected position.
         *           Spawn the ball to OG position.
         *           (separation of concerns)
         *      
        */
        CorrectedPosition = null;
        UserModifiedPosition = false;
    }
}

[Serializable, System.Flags]
public enum UserOverrides : byte
{
    UserModifiedPosition = 1,
    UserModifiedType = 2
}

[Serializable]
public class ExerciseData
{
    private const byte SMALLEST_EXERCISE_ID = 1;
    private const byte LARGET_EXERCISE_ID = 10;
    public ExerciseData(uint? excerciseId, byte numberOfAttempts = 10)
    {
        if (excerciseId == null)
            throw new ArgumentNullException("Exercise ID cannot be null.");
        if (excerciseId < SMALLEST_EXERCISE_ID && excerciseId > LARGET_EXERCISE_ID)
            throw new ArgumentOutOfRangeException($"Min exercise id {excerciseId}, max excercise id {LARGET_EXERCISE_ID}.");
        ExcerciseId = excerciseId;
        Attemps = new(numberOfAttempts);
    }


    uint? ExcerciseId { get; } = null;

    public readonly List<Attempt> Attemps;

    public static byte GetMaxNumberOfNeededBallForExercise(byte exerciseId)
    {
        const byte MIN_REQUIRED_BALLS_ON_TABLE = 2;

        return MIN_REQUIRED_BALLS_ON_TABLE;
    }
}

[Serializable]
public class Attempt
{
    public bool QuestWasUsed { get; } = false;

    public string DurationSeconds = string.Empty;
}
