public class UserSettings
{
    public string SelectedLabel { get; set; } = SemanticLabel.OTHER;

    public ApiMode ApiMode { get; set; } = ApiMode.SemanticLabeling;

    public ScanControl ScanControl { get; set; } = ScanControl.ReScanScene;

    public bool AllowControllerFallBack { get; set; } = false;

    public EnvironmentInfo EnviromentInfo { get; set; } = null;

    public DeviceInformation DeviceInformation { get; set; } = DeviceInformation.PrimaryQuest;

    public GameplayControlSettings GameplayControlSettings { get; set; } = new();

    public void EnsureDefaults()
    {
        GameplayControlSettings ??= new GameplayControlSettings();
        GameplayControlSettings.Normalize();
    }

    public byte GetXAxisCorrectionStepMM() =>
        GameplayControlSettings?.XAxisCorrectionStepMM ?? GameplayControlSettings.DefaultCorrectionStepMM;

    public byte GetZAxisCorrectionStepMM() =>
        GameplayControlSettings?.ZAxisCorrectionStepMM ?? GameplayControlSettings.DefaultCorrectionStepMM;
}