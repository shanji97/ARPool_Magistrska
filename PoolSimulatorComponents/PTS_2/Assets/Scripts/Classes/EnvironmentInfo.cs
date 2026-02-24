using Newtonsoft.Json;
using System;

[Serializable]
public class EnvironmentInfo
{
    [JsonProperty("_schema_version")]
    public byte _schema_version { get; set; }

    [JsonProperty("table")]
    public Table Table { get; set; }

    [JsonProperty("pockets")]
    public Pockets Pockets { get; set; }

    [JsonProperty("ball_spec")]
    public BallSpec BallSpec { get; set; }

    [JsonProperty("camera")]
    public CameraData CameraData { get; set; }
}

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

[Serializable]
public class Pockets
{
    [JsonProperty("corner_pocket_diameter_mm ")]
    public short CornerPocketDiameterMM { get; set; }

    [JsonProperty("side_pocket_diameter_mm")]
    public short SidePocketDiameterMM { get; set; }

    [JsonProperty("corner_jaw_diameter_mm ")]
    public short CornerJawDiameterMM { get; set; }

    [JsonProperty("side_jaw_diameters_mm")]
    public int SideJawDiametersMM { get; set; }
}

[Serializable]
public class BallSpec
{
    [JsonProperty("diameter_m")]
    public float DiameterM { get; set; } = 0.05715f;

    [JsonProperty("ball_circumference_m")]
    public float BallCircumferenceM { get; set; } = .068f;
}

[Serializable]
public class CameraData
{
    [JsonProperty("height_from_floor_m")]
    public float HeightFromFloorM { get; set; }
}