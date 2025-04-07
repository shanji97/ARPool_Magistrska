using UnityEngine.UI;

public class UserSettings
{
    public string SelectedLabel { get; set; } = SemanticLabel.OTHER;

    public ApiMode ApiMode { get; set; } = ApiMode.SemanticLabeling;

    public ScanControl ScanControl { get; set; } = ScanControl.ReScanScene;

    public PoolTable DefaultTable { get; set; } = new PoolTable(TableType.Small);
}

public enum ScanControl
{
    ReScanScene = 0,
    NoAction = 1
}