using Newtonsoft.Json;
using System;

[Serializable]
public class Table
{
    private const byte MaxArrayLenght = 3;

    public Table(float length, float width, float height, short? overallLength = null, short? overallWidth = null, byte[] clothLowerHSV = null, byte[] clothUpperHSV = null)
    {
        PlayfieldMM = new float[MaxArrayLenght] { length, width, height };

        if (overallLength != null && overallWidth != null)
            OverallMM = new short[2] { overallLength.Value, overallWidth.Value };
        if (clothLowerHSV?.Length == 3)
            ClothLowerHsv = clothLowerHSV;
        if (clothUpperHSV?.Length == 3)
            ClothUpperHsv = clothUpperHSV;
    }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("playfield_mm")]
    public float[] PlayfieldMM { private get; set; } // Length [0], Width [1], Height [2]

    public float Length
    {
        get
        {
            if (PlayfieldMM?.Length < MaxArrayLenght)
                return -1;
            else
                return PlayfieldMM[0];
        }
    }

    public float Width
    {
        get
        {
            if (PlayfieldMM?.Length < MaxArrayLenght)
                return -1;
            else
                return PlayfieldMM[1];
        }
    }

    public float Height
    {
        get
        {
            if (PlayfieldMM?.Length < MaxArrayLenght)
                return -1;
            else
                return PlayfieldMM[2];
        }
    }

    [JsonProperty("overall_mm")]
    public short[] OverallMM { get; set; } // Length [0], Width [1] 

    [JsonProperty("notes")]
    public string Notes { get; set; }

    [JsonProperty("cloth_profile")]
    public string ClothProfile { get; set; }

    [JsonProperty("cloth_lower_hsv")]
    public byte[] ClothLowerHsv { get; set; }

    [JsonProperty("cloth_upper_hsv")]
    public byte[] ClothUpperHsv { get; set; }
}
