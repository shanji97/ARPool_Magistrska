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
}