namespace ThermoTracker.ThermoTracker.Models;

public class FileLoggingInfo
{
    public string CurrentLogFilePath { get; set; } = string.Empty;
    public long CurrentLogFileSizeBytes { get; set; }
    public int TotalLogFiles { get; set; }
    public List<string> LogFiles { get; set; } = [];
    public int CurrentFileEntryCount { get; set; }
    public string Format { get; set; } = string.Empty;
}