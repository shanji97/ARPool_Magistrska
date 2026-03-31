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
    public Pocket Pockets { get; set; }

    [JsonProperty("ball_spec")]
    public BallSpec BallSpec { get; set; }

    [JsonProperty("camera")]
    public CameraData CameraData { get; set; }
}
