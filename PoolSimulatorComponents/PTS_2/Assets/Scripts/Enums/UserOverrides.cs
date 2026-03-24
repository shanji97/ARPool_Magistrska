using System;

[Serializable, Flags]
public enum UserOverrides : byte
{
    None = 0,
    UserModifiedPosition = 1,
    UserModifiedType = 2,
    UserModifiedBallId = 4,
    UserIgnoredBall = 8
}