using System;

public static class BallTypeWire
{
    public static bool TryParseToken(ReadOnlySpan<char> token, out BallType type)
    {
        token = token.Trim();

        if (token.SequenceEqual("c"))
        {
            type = BallType.Cue;
            return true;
        }

        if (token.SequenceEqual("e"))
        {
            type = BallType.Eight;
            return true;
        }

        if (token.SequenceEqual("so"))
        {
            type = BallType.Solid;
            return true;
        }

        if (token.SequenceEqual("st"))
        {
            type = BallType.Stripe;
            return true;
        }

        type = default;
        return false;
    }

    public static bool TryParseBallType(string ballType, out BallType type)
    {
        type = default;

        if (string.IsNullOrWhiteSpace(ballType))
            return false;

        string normalized = ballType.Trim();

        if (normalized.Equals("cue", StringComparison.OrdinalIgnoreCase))
        {
            type = BallType.Cue;
            return true;
        }

        if (normalized.Equals("eight", StringComparison.OrdinalIgnoreCase))
        {
            type = BallType.Eight;
            return true;
        }

        if (normalized.Equals("solid", StringComparison.OrdinalIgnoreCase))
        {
            type = BallType.Solid;
            return true;
        }

        if (normalized.Equals("stripe", StringComparison.OrdinalIgnoreCase))
        {
            type = BallType.Stripe;
            return true;
        }

        return false;
    }

    public static bool TryParseBallType(string ballType, out byte typeNumber)
    {
        bool parsed = TryParseBallType(ballType, out BallType type);
        typeNumber = parsed ? (byte)type : byte.MaxValue;
        return parsed;
    }
}