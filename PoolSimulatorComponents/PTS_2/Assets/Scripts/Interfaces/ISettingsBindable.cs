public interface ISettingsBindable
{
    public void LoadFromSettings(UserSettings settings);
    public void SaveToSettings(UserSettings settings);
}