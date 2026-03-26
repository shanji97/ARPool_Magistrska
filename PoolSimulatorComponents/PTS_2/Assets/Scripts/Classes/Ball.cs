using System;
using UnityEngine;

[Serializable]
public class Ball
{
    public BallType BallType;

    public byte BallId { get; private set; }

    public Vector2Float DetectedPosition;

    public Vector2Float CorrectedPosition { get; private set; } = null;

    public UserOverrides UserOverrides { get; private set; } = UserOverrides.None;

    private BallType _lastDetectedBallType;
    private byte _lastDetectedBallId;
    private Vector2Float _lastDetectedPosition;
    private bool _hasDetectedBaseline;

    public bool HasDetectedBaseline() => _hasDetectedBaseline;

    public bool IsPositionUserOverriden() =>
        (UserOverrides & UserOverrides.UserModifiedPosition) == UserOverrides.UserModifiedPosition;

    public bool IsTypeUserOverriden() =>
        (UserOverrides & UserOverrides.UserModifiedType) == UserOverrides.UserModifiedType;

    public bool IsBallIdUserOverriden() =>
        (UserOverrides & UserOverrides.UserModifiedBallId) == UserOverrides.UserModifiedBallId;

    public bool IsIgnoredByUser() =>
        (UserOverrides & UserOverrides.UserIgnoredBall) == UserOverrides.UserIgnoredBall;

    public Vector2Float GetEffectivePosition() =>
        IsPositionUserOverriden() && CorrectedPosition != null
            ? CorrectedPosition
            : DetectedPosition;

    public BallType GetLastDetectedOrCurrentBallType() =>
        _hasDetectedBaseline ? _lastDetectedBallType : BallType;

    public BallType GetDetectedOrCurrentBallType() =>
        _hasDetectedBaseline ? _lastDetectedBallType : BallType;

    public byte GetDetectedOrCurrentBallId() =>
        _hasDetectedBaseline
            ? NormalizeBallIdForType(_lastDetectedBallId, _lastDetectedBallType)
            : BallId;

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
        {
            BallId = IsTypeUserOverriden()
                ? NormalizeBallIdForType(BallId, BallType)
                : NormalizeBallIdForType(detectedBallId, detectedType);
        }
    }

    public void AssignBallId(byte ballId) =>
        BallId = NormalizeBallIdForType(ballId, BallType);

    public void OverrideBallType(BallType newBallType)
    {
        BallType = newBallType;
        UserOverrides |= UserOverrides.UserModifiedType;

        BallId = IsBallIdUserOverriden()
            ? NormalizeBallIdForType(BallId, newBallType)
            : GetDefaultBallIdForType(newBallType);
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
    }

    public void SetIgnoredByUser(bool isIgnored)
    {
        if (isIgnored)
            UserOverrides |= UserOverrides.UserIgnoredBall;
        else
            UserOverrides &= ~UserOverrides.UserIgnoredBall;
    }

    public void ReleasePositionOverride()
    {
        CorrectedPosition = null;
        UserOverrides &= ~UserOverrides.UserModifiedPosition;
    }

    public void ReleaseTypeOverride()
    {
        UserOverrides &= ~UserOverrides.UserModifiedType;

        if (_hasDetectedBaseline)
            BallType = _lastDetectedBallType;

        if (IsBallIdUserOverriden())
            BallId = NormalizeBallIdForType(BallId, BallType);
        else if (_hasDetectedBaseline)
            BallId = NormalizeBallIdForType(_lastDetectedBallId, BallType);
        else
            BallId = GetDefaultBallIdForType(BallType);
    }

    public void ReleaseBallIdOverride()
    {
        UserOverrides &= ~UserOverrides.UserModifiedBallId;
        BallId = _hasDetectedBaseline
            ? NormalizeBallIdForType(_lastDetectedBallId, BallType)
            : GetDefaultBallIdForType(BallType);
    }

    public void ReleaseIgnoreOverride() =>
        UserOverrides &= ~UserOverrides.UserIgnoredBall;

    public void ResetUserOverrides()
    {
        ReleasePositionOverride();
        ReleaseIgnoreOverride();

        UserOverrides &= ~(UserOverrides.UserModifiedType | UserOverrides.UserModifiedBallId);

        if (_hasDetectedBaseline)
        {
            BallType = _lastDetectedBallType;
            BallId = NormalizeBallIdForType(_lastDetectedBallId, _lastDetectedBallType);
            DetectedPosition = _lastDetectedPosition;
        }
    }

    private static byte GetDefaultBallIdForType(BallType ballType) =>
        ballType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            BallType.Solid => 1,
            BallType.Stripe => 9,
            _ => 0
        };

    private static byte NormalizeBallIdForType(byte incomingBallId, BallType ballType) =>
        ballType switch
        {
            BallType.Cue => 0,
            BallType.Eight => 8,
            BallType.Solid => (byte)Mathf.Clamp(incomingBallId, 1, 7),
            BallType.Stripe => (byte)Mathf.Clamp(incomingBallId, 9, 15),
            _ => incomingBallId
        };
}