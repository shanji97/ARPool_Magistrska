using System;

[Serializable]
public class Attempt
{
    public bool QuestWasUsed { get; } = false;

    public string DurationSeconds = string.Empty;
}