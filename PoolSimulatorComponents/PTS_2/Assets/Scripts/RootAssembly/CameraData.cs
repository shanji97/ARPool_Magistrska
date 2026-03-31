using Newtonsoft.Json;
using System;

[Serializable]
public class CameraData
{
    [JsonProperty("height_from_floor_m")]
    public float HeightFromFloorM { get; set; }
}