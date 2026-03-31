using Newtonsoft.Json;
using System;

[Serializable]
public class BallSpec
{
    [JsonProperty("diameter_m")]
    public float DiameterM { get; set; } = 0.05715f;

    [JsonProperty("ball_circumference_m")]
    public float BallCircumferenceM { get; set; } = .068f;
}
