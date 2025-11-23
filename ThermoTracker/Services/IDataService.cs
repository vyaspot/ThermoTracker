using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public interface IDataService
{
    Task StoreDataAsync(SensorData data);
    Task<List<SensorData>> GetRecentDataAsync(string sensorName, int count);
    Task LogDataAsync(SensorData data);
    Task<List<SensorData>> GetDataHistoryAsync(string sensorName, TimeSpan duration);
    Task<List<SensorData>> GetAnomaliesAsync(TimeSpan duration);
    Task<List<SensorData>> GetSensorDataByDateRangeAsync(string sensorName, DateTime start, DateTime end);
    Task<int> CleanOldDataAsync(TimeSpan olderThan);
    Task<SensorStatistics> GetSensorStatisticsAsync(string sensorName, TimeSpan duration);
    Task<OverallStatistics> GetOverallStatisticsAsync(TimeSpan duration);
    Task<List<SensorAlert>> GetRecentAlertsAsync(int count);
    Task<List<SensorData>> GetFaultyReadingsAsync(TimeSpan duration);
}