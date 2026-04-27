using Newtonsoft.Json;
using System;

[Serializable]
public class HsvRange
{
    public const byte HSV_ARRAY_LENGTH = 3;
    public const byte HUE_MIN = 0;
    public const byte HUE_MAX = 179;
    public const byte CHANNEL_MIN = 0;
    public const byte CHANNEL_MAX = 255;

    public HsvRange(string name, byte[] lowerHsv, byte[] upperHsv, bool enabled = true)
    {
        Name = name;
        if (lowerHsv?.Length == HSV_ARRAY_LENGTH)
            Lower = ClampHsv(lowerHsv);
        if (upperHsv?.Length == HSV_ARRAY_LENGTH)
            Upper = ClampHsv(upperHsv);
        Enabled = enabled;
    }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("lower")]
    public byte[] Lower { get; set; }

    [JsonProperty("upper")]
    public byte[] Upper { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    public bool IsValid => IsValidHsvArray(Lower) && IsValidHsvArray(Upper);

    public static bool IsValidHsvArray(byte[] values) => values?.Length == HSV_ARRAY_LENGTH;

    public static byte[] ClampHsv(byte[] values)
    {
        if (!IsValidHsvArray(values))
            return null;

        return new[]
        {
            ClampByte(values[0], HUE_MIN, HUE_MAX),
            ClampByte(values[1], CHANNEL_MIN, CHANNEL_MAX),
            ClampByte(values[2], CHANNEL_MIN, CHANNEL_MAX)
        };
    }
    public static byte ClampByte(byte value, byte minimum, byte maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}

