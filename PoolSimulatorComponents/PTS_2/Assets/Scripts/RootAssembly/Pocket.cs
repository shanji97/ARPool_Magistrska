using Newtonsoft.Json;
using System;

[Serializable]
public class Pocket
{
    [JsonProperty("corner_pocket_diameter_mm")]
    public short CornerPocketDiameterMM { get; set; }

    [JsonProperty("side_pocket_diameter_mm")]
    public short SidePocketDiameterMM { get; set; }

    [JsonProperty("corner_jaw_diameter_mm")]
    public short CornerJawDiameterMM { get; set; }

    [JsonProperty("side_jaw_diameters_mm")]
    public int SideJawDiametersMM { get; set; }
}
