using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

public class GameplayControlSettings
{
    public const byte DefaultCorrectionStepMM = 1;

    private static readonly byte[] AllowedCorrectionStepsMM = { 1, 5, 10 };

    public GameplayControlSettings(
        byte xAxisCorrectionStepMM = DefaultCorrectionStepMM,
        byte zAxisCorrectionStepMM = DefaultCorrectionStepMM)
    {
        SetAxisCorrectionStep(xAxisCorrectionStepMM, zAxisCorrectionStepMM);
    }

    public byte XAxisCorrectionStepMM = DefaultCorrectionStepMM;

    public byte ZAxisCorrectionStepMM = DefaultCorrectionStepMM;

    public void SetXAxisCorrectionStep(byte newXAxisCorrectionStep) =>
        XAxisCorrectionStepMM = NormalizeCorrectionStep(newXAxisCorrectionStep);

    public void SetZAxisCorrectionStep(byte newZAxisCorrectionStep) =>
        ZAxisCorrectionStepMM = NormalizeCorrectionStep(newZAxisCorrectionStep);

    public void SetAxisCorrectionStep(byte newXAxisCorrectionStep, byte newZAxisCorrectionStep)
    {
        SetXAxisCorrectionStep(newXAxisCorrectionStep);
        SetZAxisCorrectionStep(newZAxisCorrectionStep);
    }

    public void Normalize()
    {
        XAxisCorrectionStepMM = NormalizeCorrectionStep(XAxisCorrectionStepMM);
        ZAxisCorrectionStepMM = NormalizeCorrectionStep(ZAxisCorrectionStepMM);
    }

    public static byte NormalizeCorrectionStep(byte value) =>
        AllowedCorrectionStepsMM.Contains(value) ? value : DefaultCorrectionStepMM;

    public static string ToDisplayValue(byte value) => $"{NormalizeCorrectionStep(value)}mm";

    public static bool TryParseDisplayValue(string value, out byte stepMm)
    {
        stepMm = DefaultCorrectionStepMM;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim()
            .Replace(" ", string.Empty)
            .Replace("mm", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!byte.TryParse(normalized, out byte parsed))
            return false;

        stepMm = NormalizeCorrectionStep(parsed);
        return true;
    }

    public static List<string> GetDisplayOptions() =>
        AllowedCorrectionStepsMM.Select(ToDisplayValue).ToList();
}