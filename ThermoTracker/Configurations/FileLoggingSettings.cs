namespace ThermoTracker.ThermoTracker.Configurations;

public class FileLoggingSettings
{
    public string LogDirectory { get; set; } = "Logs";
    public string LogFileName { get; set; } = "Sensor_Readings_{date}.txt";
    public int MaxFileSizeMB { get; set; } = 10;
    public int RetentionDays { get; set; } = 7;
    public bool EnableRotation { get; set; } = true;
    public bool IncludeHeader { get; set; } = true;
    public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public bool UseHumanReadableFormat { get; set; } = true;
}