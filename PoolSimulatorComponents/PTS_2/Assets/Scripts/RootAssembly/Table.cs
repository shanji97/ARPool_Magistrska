using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

[Serializable]
public class Table
{
    public const byte MAX_ARRAY_LENGTH = 3;
    public const byte OVERALL_ARRAY_LENGTH = 2;

    public Table()
    {
        ClothHsvRanges = new List<HsvRange>(); // Schema v3: prevents null-list errors for old JSON files.
    }

    public Table(
        float length,
        float width,
        float height,
        short? overallLength = null,
        short? overallWidth = null,
        byte[] clothLowerHSV = null,
        byte[] clothUpperHSV = null,
        HsvRange[] clothHsvRanges = null)
    {
        PlayfieldMM = new float[MAX_ARRAY_LENGTH] { length, width, height };
        ClothHsvRanges = new List<HsvRange>(); // Schema v3: always initialize the list.

        if (overallLength != null && overallWidth != null)
            OverallMM = new short[OVERALL_ARRAY_LENGTH] { overallLength.Value, overallWidth.Value };

        if (HsvRange.IsValidHsvArray(clothLowerHSV))
            ClothLowerHsv = HsvRange.ClampHsv(clothLowerHSV);

        if (HsvRange.IsValidHsvArray(clothUpperHSV))
            ClothUpperHsv = HsvRange.ClampHsv(clothUpperHSV);

        if (clothHsvRanges?.Length > 0)
            ClothHsvRanges = clothHsvRanges
                .Where(range => range != null && range.IsValid)
                .Select(range => new HsvRange(range.Name, range.Lower, range.Upper, range.Enabled))
                .ToList();

        ConvertSeparateRangesToArray(clearLegacyFields: false); // Schema v3: converts constructor legacy HSV values if needed.
    }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("playfield_mm")]
    public float[] PlayfieldMM { get; set; }

    public float Length => PlayfieldMM == null || PlayfieldMM.Length < MAX_ARRAY_LENGTH ? -1f : PlayfieldMM[0];

    public float Width => PlayfieldMM == null || PlayfieldMM.Length < MAX_ARRAY_LENGTH ? -1f : PlayfieldMM[1];

    public float Height => PlayfieldMM == null || PlayfieldMM.Length < MAX_ARRAY_LENGTH ? -1f : PlayfieldMM[2];

    [JsonProperty("overall_mm")]
    public short[] OverallMM { get; set; }

    [JsonProperty("notes")]
    public string Notes { get; set; }

    [JsonProperty("cloth_profile")]
    public string ClothProfile { get; set; }

    [JsonProperty("cloth_lower_hsv")]
    public byte[] ClothLowerHsv { get; set; }

    [JsonProperty("cloth_upper_hsv")]
    public byte[] ClothUpperHsv { get; set; }

    [JsonProperty("cloth_hsv_ranges")]
    public List<HsvRange> ClothHsvRanges { get; set; } = new();

    [OnDeserialized]
    public void OnDeserialized(StreamingContext context) =>
        ConvertSeparateRangesToArray(clearLegacyFields: false); // Schema v3: auto-convert old profile fields after Newtonsoft.Json loading.

    public void ConvertSeparateRangesToArray(bool clearLegacyFields = false)
    {
        if (!HsvRange.IsValidHsvArray(ClothLowerHsv) || !HsvRange.IsValidHsvArray(ClothUpperHsv))
            return;

        ClothHsvRanges ??= new List<HsvRange>(); // Schema v3: protects old schema v2 JSON files.

        var alreadyExists = ClothHsvRanges.Any(range =>
            range != null &&
            HsvRange.IsValidHsvArray(range.Lower) &&
            HsvRange.IsValidHsvArray(range.Upper) &&
            range.Lower.SequenceEqual(ClothLowerHsv) &&
            range.Upper.SequenceEqual(ClothUpperHsv));

        if (alreadyExists)
            return;

        var itemCount = ClothHsvRanges.Count;
        var rangeName = string.IsNullOrWhiteSpace(ClothProfile)
            ? $"legacy_hsv_range_{itemCount + 1}"
            : $"{ClothProfile}_{itemCount + 1}";

        ClothHsvRanges.Add(new HsvRange(
            rangeName,
            ClothLowerHsv,
            ClothUpperHsv,
            true));

        if (!clearLegacyFields)
            return;

        ClothLowerHsv = null;
        ClothUpperHsv = null;
    }

    public IReadOnlyList<HsvRange> GetEnabledClothHsvRanges()
    {
        var schemaV3Ranges = ClothHsvRanges?
            .Where(range => range != null && range.Enabled && range.IsValid)
            .Select(range => new HsvRange(range.Name, range.Lower, range.Upper, range.Enabled))
            .ToList();

        if (schemaV3Ranges?.Count > 0)
            return schemaV3Ranges;

        if (HsvRange.IsValidHsvArray(ClothLowerHsv) && HsvRange.IsValidHsvArray(ClothUpperHsv))
        {
            return new List<HsvRange>
            {
                new(
                    string.IsNullOrWhiteSpace(ClothProfile) ? "legacy_hsv_range" : ClothProfile,
                    ClothLowerHsv,
                    ClothUpperHsv,
                    true)
            };
        }

        return Array.Empty<HsvRange>();
    }

    public HsvRange GetPrimaryClothHsvRange() => GetEnabledClothHsvRanges().FirstOrDefault();
}