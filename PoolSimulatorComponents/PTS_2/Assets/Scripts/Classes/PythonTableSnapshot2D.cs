using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public class PythonTableSnapshot2D
{
    public string EnvironmentKey;
    public double ReceivedAtUtcUnixSeconds;
    public int Revision;
    public bool HasValidSourceFrame;

    public TableFrame2D SourceFrame;
    public List<DetectedPocketSnapshot2D> RawPockets = new();
    public List<DetectedBallSnapshot2D> RawBalls = new();

    public bool HasCueStick;
    public DetectedCueStickSnapshot2D RawCueStick;

    public bool HasAnyBalls() => RawBalls?.Count > 0;
    public bool HasAllSixPockets() => RawPockets?.Count == 6;

    public bool HasAnyReprojectableContent() => RawBalls.Any() || RawPockets.Any() || HasCueStick;
    public bool CanReproject() => HasValidSourceFrame && HasAnyReprojectableContent();

    public void Clear()
    {
        EnvironmentKey = string.Empty;
        ReceivedAtUtcUnixSeconds = 0d;
        Revision = 0;
        HasValidSourceFrame = false;
        SourceFrame = default;

        RawPockets.Clear();
        RawBalls.Clear();

        HasCueStick = false;
        RawCueStick = default;
    }

    public void ReplacePockets(IEnumerable<DetectedPocketSnapshot2D> pockets)
    {
        RawPockets.Clear();

        if (pockets == null)
            return;

        RawPockets.AddRange(
            pockets
                .Where(p => p.IsValid())
                .OrderBy(p => p.PocketIndex));
    }

    public void ReplaceBalls(IEnumerable<DetectedBallSnapshot2D> balls)
    {
        RawBalls.Clear();

        if (balls == null)
            return;

        RawBalls.AddRange(balls.Where(b => b.IsValid()));
    }

    public void ReplaceCueStick(DetectedCueStickSnapshot2D cueStick)
    {
        HasCueStick = cueStick.IsValid();
        RawCueStick = HasCueStick ? cueStick : default;
    }

    public void ClearCueStick()
    {
        HasCueStick = false;
        RawCueStick = default;
    }

    public void StampNewRevision(string environmentKey = null)
    {
        Revision++;
        EnvironmentKey = environmentKey ?? EnvironmentKey ?? string.Empty;
        ReceivedAtUtcUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    }
}