using Newtonsoft.Json;
using System;

[Serializable]
public class EnvironmentInfo
{
    [JsonProperty("table")]
    public Table PoolTable { get; set; } = new();

    [JsonProperty("camera")]
    public Camera CameraCharacteristics { get; set; } = new();

    public class Table
    {
        [JsonProperty("L_m")] public float L_m { get; set; }
        [JsonProperty("W_m")] public float W_m { get; set; }
        [JsonProperty("H_m")] public float H_m { get; set; }
        [JsonProperty("B_m")] public float BallDiameter_m { get; set; } = .05715f;
    }

    public class Camera
    {
        [JsonProperty("HFromFloor_m")] public float HFromFloor_m { get; set; } = 2.5f;
    }
}
