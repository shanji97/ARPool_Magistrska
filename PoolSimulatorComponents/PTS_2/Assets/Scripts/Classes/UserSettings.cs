public class UserSettings
{
    public string SelectedLabel { get; set; } = SemanticLabel.OTHER;

    public ApiMode ApiMode { get; set; } = ApiMode.SemanticLabeling;

    public ScanControl ScanControl { get; set; } = ScanControl.ReScanScene;

    public bool AllowControllerFallBack { get; set; } = false;

    public EnvironmentInfo TableInfo {get; set;}  = null;
}