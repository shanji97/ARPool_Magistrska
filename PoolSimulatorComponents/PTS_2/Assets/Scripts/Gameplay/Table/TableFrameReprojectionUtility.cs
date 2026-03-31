using System.Collections.Generic;

public static class TableFrameReprojectionUtility
{
    public static bool TryReprojectXZ(
        Vector2Float sourceWorldXZ,
        TableFrame2D sourceFrame,
        TableFrame2D targetFrame,
        out Vector2Float targetWorldXZ)
    {
        targetWorldXZ = default;

        if (sourceWorldXZ == null)
            return false;

        if (!sourceFrame.IsValid() || !targetFrame.IsValid())
            return false;

        Vector2Float sourceLocal = sourceFrame.WorldToLocal(sourceWorldXZ);
        Vector2Float uv = sourceFrame.LocalMetersToNormalized(sourceLocal);
        Vector2Float clampedUv = sourceFrame.ClampNormalized(uv);
        Vector2Float targetLocal = targetFrame.NormalizedToLocalMeters(clampedUv);
        targetWorldXZ = targetFrame.LocalToWorld(targetLocal);

        return targetWorldXZ != null;
    }

    public static bool TryReprojectBall(
        DetectedBallSnapshot2D sourceBall,
        TableFrame2D sourceFrame,
        TableFrame2D targetFrame,
        out DetectedBallSnapshot2D reprojectedBall)
    {
        reprojectedBall = default;

        if (!sourceBall.IsValid())
            return false;

        if (!TryReprojectXZ(sourceBall.PositionXZ, sourceFrame, targetFrame, out Vector2Float targetPositionXZ))
            return false;

        reprojectedBall = new DetectedBallSnapshot2D(
            sourceBall.BallType,
            sourceBall.RawIncomingId,
            targetPositionXZ,
            sourceBall.Confidence,
            sourceBall.VelocityXZ);

        return reprojectedBall.IsValid();
    }

    public static bool TryReprojectPocket(
        DetectedPocketSnapshot2D sourcePocket,
        TableFrame2D sourceFrame,
        TableFrame2D targetFrame,
        out DetectedPocketSnapshot2D reprojectedPocket)
    {
        reprojectedPocket = default;

        if (!sourcePocket.IsValid())
            return false;

        if (!TryReprojectXZ(sourcePocket.PositionXZ, sourceFrame, targetFrame, out Vector2Float targetPositionXZ))
            return false;

        reprojectedPocket = new DetectedPocketSnapshot2D(
            sourcePocket.PocketIndex,
            targetPositionXZ);

        return reprojectedPocket.IsValid();
    }

    public static bool TryReprojectCueStick(
        DetectedCueStickSnapshot2D sourceCueStick,
        TableFrame2D sourceFrame,
        TableFrame2D targetFrame,
        out DetectedCueStickSnapshot2D reprojectedCueStick)
    {
        reprojectedCueStick = default;

        if (!sourceCueStick.IsValid())
            return false;

        if (!TryReprojectXZ(sourceCueStick.LinePointXZ, sourceFrame, targetFrame, out Vector2Float targetLinePointXZ))
            return false;

        if (!TryReprojectXZ(sourceCueStick.HitPointXZ, sourceFrame, targetFrame, out Vector2Float targetHitPointXZ))
            return false;

        float directionX = targetHitPointXZ.X - targetLinePointXZ.X;
        float directionZ = targetHitPointXZ.Y - targetLinePointXZ.Y;
        float magnitude = UnityEngine.Mathf.Sqrt((directionX * directionX) + (directionZ * directionZ));

        if (magnitude <= 0.0001f)
            return false;

        Vector2Float targetDirectionXZ = new Vector2Float(directionX / magnitude, directionZ / magnitude);

        reprojectedCueStick = new DetectedCueStickSnapshot2D(
            targetLinePointXZ,
            targetDirectionXZ,
            targetHitPointXZ,
            sourceCueStick.Confidence);

        return reprojectedCueStick.IsValid();
    }

    public static List<DetectedBallSnapshot2D> ReprojectBalls(
        IReadOnlyList<DetectedBallSnapshot2D> sourceBalls,
        TableFrame2D sourceFrame,
        TableFrame2D targetFrame)
    {
        List<DetectedBallSnapshot2D> result = new(sourceBalls?.Count ?? 0);

        if (sourceBalls == null || sourceBalls.Count == 0)
            return result;

        if (!sourceFrame.IsValid() || !targetFrame.IsValid())
            return result;

        for (int i = 0; i < sourceBalls.Count; i++)
        {
            if (TryReprojectBall(sourceBalls[i], sourceFrame, targetFrame, out DetectedBallSnapshot2D reprojectedBall))
                result.Add(reprojectedBall);
        }

        return result;
    }

    public static List<DetectedPocketSnapshot2D> ReprojectPockets(
        IReadOnlyList<DetectedPocketSnapshot2D> sourcePockets,
        TableFrame2D sourceFrame,
        TableFrame2D targetFrame)
    {
        List<DetectedPocketSnapshot2D> result = new(sourcePockets?.Count ?? 0);

        if (sourcePockets == null || sourcePockets.Count == 0)
            return result;

        if (!sourceFrame.IsValid() || !targetFrame.IsValid())
            return result;

        for (int i = 0; i < sourcePockets.Count; i++)
        {
            if (TryReprojectPocket(sourcePockets[i], sourceFrame, targetFrame, out DetectedPocketSnapshot2D reprojectedPocket))
                result.Add(reprojectedPocket);
        }

        return result;
    }

    public static bool TryReprojectSnapshot(
        PythonTableSnapshot2D sourceSnapshot,
        TableFrame2D targetFrame,
        out PythonTableSnapshot2D reprojectedSnapshot)
    {
        reprojectedSnapshot = null;

        if (sourceSnapshot == null)
            return false;

        if (!sourceSnapshot.CanReproject())
            return false;

        if (!targetFrame.IsValid())
            return false;

        PythonTableSnapshot2D output = new()
        {
            EnvironmentKey = sourceSnapshot.EnvironmentKey,
            ReceivedAtUtcUnixSeconds = sourceSnapshot.ReceivedAtUtcUnixSeconds,
            Revision = sourceSnapshot.Revision,
            HasValidSourceFrame = true,
            SourceFrame = targetFrame
        };

        output.ReplacePockets(ReprojectPockets(sourceSnapshot.RawPockets, sourceSnapshot.SourceFrame, targetFrame));
        output.ReplaceBalls(ReprojectBalls(sourceSnapshot.RawBalls, sourceSnapshot.SourceFrame, targetFrame));

        if (sourceSnapshot.HasCueStick &&
            TryReprojectCueStick(sourceSnapshot.RawCueStick, sourceSnapshot.SourceFrame, targetFrame, out DetectedCueStickSnapshot2D reprojectedCueStick))
        {
            output.ReplaceCueStick(reprojectedCueStick);
        }
        else
        {
            output.ClearCueStick();
        }

        reprojectedSnapshot = output;
        return true;
    }
}