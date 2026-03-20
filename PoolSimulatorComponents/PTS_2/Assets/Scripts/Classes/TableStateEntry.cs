using System;
using System.Collections.Generic;
using UnityEngine;

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

        const int MAX_REQUIRED_BALLS_ON_TABLE = 16;
        int requiredBallCapacity = MAX_REQUIRED_BALLS_ON_TABLE;

        requiredBallCapacity = GameMode switch
        {
            global::GameMode.SoloGame or global::GameMode.OnlineCoop or global::GameMode.LocalCoop => MAX_REQUIRED_BALLS_ON_TABLE,

            global::GameMode.LessonsMode => ExerciseData.GetMaxNumberOfNeededBallForExercise(
                                excerciseId ?? throw new ArgumentNullException(nameof(excerciseId))),
            _ => MAX_REQUIRED_BALLS_ON_TABLE,
        };

        // UPDATED: explicit list allocation, still preserving your current variable name and semantics.
        BallInfo = new List<Ball>(requiredBallCapacity);

        DataFromDevice = deviceInformation;
    }

    public DateTime EntryDate { get; } = DateTime.Now;

    // UPDATED: private setter so constructor can assign safely.
    public global::GameMode GameMode { get; } = global::GameMode.SoloGame;

    // NOTE: typo preserved intentionally because you already use this name elsewhere.
    public ExerciseData ExcreciseData { get; private set; } = null;

    public List<Ball> BallInfo { get; private set; } = null;

    public DeviceInformation DataFromDevice { get; private set; } = DeviceInformation.PrimaryQuest;

    public void ResetAllBallOverrides()
    {
        if (BallInfo == null)
            return;

        for (int i = 0; i < BallInfo.Count; i++)
        {
            BallInfo[i]?.ResetUserOverrides();
        }
    }
}

[Serializable]
public class Ball
{
    public BallType BallType;

    public byte BallId { private set; get; }

    public Vector2Float DetectedPosition;

    public Vector2Float CorrectedPosition { get; private set; } = null;

    public UserOverrides UserOverrides { get; private set; } = UserOverrides.None;

    // UPDATED: cached non-overridden Python/detector state used when overrides are cleared.
    private BallType _lastDetectedBallType;
    private byte _lastDetectedBallId;
    private Vector2Float _lastDetectedPosition;
    private bool _hasDetectedBaseline = false;

    public bool IsPositionUserOverriden() =>
        (UserOverrides & UserOverrides.UserModifiedPosition) == UserOverrides.UserModifiedPosition;

    public bool IsTypeUserOverriden() =>
        (UserOverrides & UserOverrides.UserModifiedType) == UserOverrides.UserModifiedType;

    public bool IsBallIdUserOverriden() =>
        (UserOverrides & UserOverrides.UserModifiedBallId) == UserOverrides.UserModifiedBallId;

    public Vector2Float GetEffectivePosition()
    {
        return IsPositionUserOverriden() && CorrectedPosition != null
            ? CorrectedPosition
            : DetectedPosition;
    }

    /// <summary>
    /// UPDATED: This is the one method Python / packet application code should use.
    /// Position is always refreshed from the detector.
    /// Type and ball number are refreshed only when the corresponding manual override is not locked.
    /// </summary>
    public void ApplyDetectedState(BallType detectedType, byte detectedBallId, Vector2Float detectedPosition)
    {
        _lastDetectedBallType = detectedType;
        _lastDetectedBallId = detectedBallId;
        _lastDetectedPosition = detectedPosition;
        _hasDetectedBaseline = true;

        DetectedPosition = detectedPosition;

        if (!IsTypeUserOverriden())
            BallType = detectedType;

        if (!IsBallIdUserOverriden())
            BallId = NormalizeBallIdForType(detectedBallId, detectedType);
    }

    public void AssignBallId(byte ballId)
    {
        // UPDATED: valid pool-ball numbers depend on current BallType, not enum numeric values.
        BallId = NormalizeBallIdForType(ballId, BallType);
    }

    public void OverrideBallType(BallType newBallType)
    {
        BallType = newBallType;
        UserOverrides |= UserOverrides.UserModifiedType;

        // UPDATED: if the ball number was not manually locked yet, move it to a valid default for the new type.
        if (!IsBallIdUserOverriden())
        {
            BallId = GetDefaultBallIdForType(newBallType);
        }
        else
        {
            // UPDATED: keep the user-selected number only if it is valid for the new type.
            BallId = NormalizeBallIdForType(BallId, newBallType);
        }
    }

    public void OverrideBallId(byte newBallId)
    {
        BallId = NormalizeBallIdForType(newBallId, BallType);
        UserOverrides |= UserOverrides.UserModifiedBallId;
    }

    public void ModifyPosition(Vector2Float newPosition)
    {
        CorrectedPosition = newPosition;
        UserOverrides |= UserOverrides.UserModifiedPosition;

        // Visual movement is still handled by the view/placement layer, not here.
    }

    public void MoveToOriginalPosition()
    {
        CorrectedPosition = null;
        UserOverrides &= ~UserOverrides.UserModifiedPosition;
    }

    public void ResetUserOverrides()
    {
        MoveToOriginalPosition();

        UserOverrides &= ~(UserOverrides.UserModifiedType | UserOverrides.UserModifiedBallId);

        if (_hasDetectedBaseline)
        {
            BallType = _lastDetectedBallType;
            BallId = NormalizeBallIdForType(_lastDetectedBallId, _lastDetectedBallType);
            DetectedPosition = _lastDetectedPosition;
        }
    }

    private static byte GetDefaultBallIdForType(BallType ballType)
    {
        return ballType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            BallType.Solid => 1,
            BallType.Stripe => 9,
            _ => 0
        };
    }

    private static byte NormalizeBallIdForType(byte incomingBallId, BallType ballType)
    {
        return ballType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            BallType.Solid => (byte)Mathf.Clamp(incomingBallId, 1, 7),
            BallType.Stripe => (byte)Mathf.Clamp(incomingBallId, 9, 15),
            _ => incomingBallId
        };
    }
}

[Serializable, Flags]
public enum UserOverrides : byte
{
    None = 0, // UPDATED
    UserModifiedPosition = 1,
    UserModifiedType = 2,
    UserModifiedBallId = 4 // UPDATED
}

[Serializable]
public class ExerciseData
{
    private const byte SMALLEST_EXERCISE_ID = 1;
    private const byte LARGET_EXERCISE_ID = 10;

    public ExerciseData(uint? excerciseId, byte numberOfAttempts = 10)
    {
        if (excerciseId == null)
            throw new ArgumentNullException(nameof(excerciseId), "Exercise ID cannot be null.");

        // UPDATED: range validation must use OR, not AND.
        if (excerciseId < SMALLEST_EXERCISE_ID || excerciseId > LARGET_EXERCISE_ID)
        {
            throw new ArgumentOutOfRangeException(
                nameof(excerciseId),
                $"Exercise id must be between {SMALLEST_EXERCISE_ID} and {LARGET_EXERCISE_ID}.");
        }

        ExerciseId = excerciseId;
        Attemps = new List<Attempt>(numberOfAttempts);
    }

    public uint? ExerciseId { get; } = null;

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