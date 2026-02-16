using System;

public static class BallTypeWire
{
    public static bool TryParseToken(ReadOnlySpan<char> token, out BallType type)
    {
        if (token.SequenceEqual("c")) { type = BallType.Cue; return true; }
        if (token.SequenceEqual("e")) { type = BallType.Eight; return true; }
        if (token.SequenceEqual("so")) { type = BallType.Solid; return true; }
        if (token.SequenceEqual("st")) { type = BallType.Stripe; return true; }
        type = default; return false;
    }

    public static bool TryParseBallType(string ballType, out BallType type)
    {
        if (ballType.Equals("cue")){ type = BallType.Cue; return true; }
        if (ballType.Equals("eight")) { type = BallType.Eight; return true; }
        if (ballType.Equals("solid")) { type = BallType.Solid; return true; }
        if (ballType.Equals("solid")) { type = BallType.Solid; return true; }
        type = default; return false;
    }

    public static bool TryParseBallType(string ballType, out byte typeNumber)
    {
        bool parsed = TryParseBallType(ballType, out BallType type);
        typeNumber = parsed ? (byte)type : byte.MaxValue;
        return parsed;
    }
}