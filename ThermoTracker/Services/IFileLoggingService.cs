using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public interface IFileLoggingService
{
    Task LogSensorReadingAsync(SensorData sensorData);
    Task<string> GetCurrentLogFilePathAsync();
    Task CleanOldLogFilesAsync();
    Task<IEnumerable<string>> GetLogFilesAsync();
    Task<long> GetCurrentLogFileSizeAsync();
    Task<int> GetCurrentLogFileEntryCountAsync();
}