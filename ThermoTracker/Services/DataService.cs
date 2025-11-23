using Microsoft.Extensions.Logging;
using ThermoTracker.ThermoTracker.Data;
using ThermoTracker.ThermoTracker.Models;
using Microsoft.EntityFrameworkCore;
using ThermoTracker.ThermoTracker.Enums;


namespace ThermoTracker.ThermoTracker.Services;

public class DataService : IDataService
{
    private readonly SensorDbContext _context;
    private readonly ILogger<DataService> _logger;

    public DataService(SensorDbContext context, ILogger<DataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task StoreDataAsync(SensorData data)
    {
        try
        {
            _context.SensorData.Add(data);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing sensor data for sensor {SensorName}", data.SensorName);
            throw;
        }
    }

    public async Task<List<SensorData>> GetRecentDataAsync(string sensorName, int count)
    {
        return await _context.SensorData
            .Where(d => d.SensorName == sensorName)
            .OrderByDescending(d => d.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task LogDataAsync(SensorData data)
    {
        var status = data.IsValid ? "VALID" : "INVALID";
        var alertType = data.AlertType != AlertType.None ? $" [{data.AlertType}]" : "";

        var logLevel = data.AlertType switch
        {
            AlertType.Fault => LogLevel.Error,
            AlertType.Spike => LogLevel.Warning,
            AlertType.Threshold => LogLevel.Warning,
            AlertType.Anomaly => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel,
            "Sensor {SensorName} ({Location}): {Temperature}°C - {Status}{Alert}",
            data.SensorName, data.SensorLocation, data.Temperature, status, alertType);
    }

    public async Task<List<SensorData>> GetDataHistoryAsync(string sensorName, TimeSpan duration)
    {
        var startTime = DateTime.UtcNow - duration;
        return await _context.SensorData
            .Where(d => d.SensorName == sensorName && d.Timestamp >= startTime)
            .OrderBy(d => d.Timestamp)
            .ToListAsync();
    }

    public async Task<List<SensorData>> GetAnomaliesAsync(TimeSpan duration)
    {
        var startTime = DateTime.UtcNow - duration;
        return await _context.SensorData
            .Where(d => d.Timestamp >= startTime && (d.IsAnomaly || d.AlertType != AlertType.None))
            .OrderByDescending(d => d.Timestamp)
            .ToListAsync();
    }

    public async Task<List<SensorData>> GetSensorDataByDateRangeAsync(string sensorName, DateTime start, DateTime end)
    {
        return await _context.SensorData
            .Where(d => d.SensorName == sensorName && d.Timestamp >= start && d.Timestamp <= end)
            .OrderBy(d => d.Timestamp)
            .ToListAsync();
    }

    public async Task<int> CleanOldDataAsync(TimeSpan olderThan)
    {
        var cutoffTime = DateTime.UtcNow - olderThan;
        var oldData = _context.SensorData.Where(d => d.Timestamp < cutoffTime);
        var count = await oldData.CountAsync();

        if (count > 0)
        {
            _context.SensorData.RemoveRange(oldData);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Cleaned {Count} old sensor data records older than {CutoffTime}", count, cutoffTime);
        }

        return count;
    }

    // New enhanced methods
    public async Task<SensorStatistics> GetSensorStatisticsAsync(string sensorName, TimeSpan duration)
    {
        var startTime = DateTime.UtcNow - duration;
        var data = await _context.SensorData
            .Where(d => d.SensorName == sensorName && d.Timestamp >= startTime)
            .ToListAsync();

        return new SensorStatistics
        {
            SensorName = sensorName,
            TotalReadings = data.Count,
            ValidReadings = data.Count(d => d.IsValid),
            AnomalyReadings = data.Count(d => d.IsAnomaly),
            SpikeReadings = data.Count(d => d.IsSpike),
            FaultReadings = data.Count(d => d.IsFaulty),
            AverageTemperature = data.Count > 0 ? data.Average(d => d.Temperature) : 0,
            MinTemperature = data.Count > 0 ? data.Min(d => d.Temperature) : 0,
            MaxTemperature = data.Count > 0 ? data.Max(d => d.Temperature) : 0
        };
    }

    public async Task<OverallStatistics> GetOverallStatisticsAsync(TimeSpan duration)
    {
        var startTime = DateTime.UtcNow - duration;
        var data = await _context.SensorData
            .Where(d => d.Timestamp >= startTime)
            .ToListAsync();

        var sensorNames = data.Select(d => d.SensorName).Distinct();

        return new OverallStatistics
        {
            TotalSensors = sensorNames.Count(),
            TotalReadings = data.Count,
            TotalAnomalies = data.Count(d => d.IsAnomaly),
            TotalSpikes = data.Count(d => d.IsSpike),
            TotalFaults = data.Count(d => d.IsFaulty),
            OverallAverageTemperature = data.Count > 0 ? data.Average(d => d.Temperature) : 0
        };
    }

    public async Task<List<SensorAlert>> GetRecentAlertsAsync(int count)
    {
        return await _context.SensorData
            .Where(d => d.AlertType != AlertType.None)
            .OrderByDescending(d => d.Timestamp)
            .Take(count)
            .Select(d => new SensorAlert
            {
                SensorName = d.SensorName,
                Location = d.SensorLocation,
                Timestamp = d.Timestamp,
                Temperature = d.Temperature,
                AlertType = d.AlertType,
                Message = $"Sensor {d.SensorName} reported {d.AlertType} alert with temperature {d.Temperature}°C"
            })
            .ToListAsync();
    }

    public async Task<List<SensorData>> GetFaultyReadingsAsync(TimeSpan duration)
    {
        var startTime = DateTime.UtcNow - duration;
        return await _context.SensorData
            .Where(d => d.Timestamp >= startTime && (d.IsFaulty || d.IsSpike))
            .OrderByDescending(d => d.Timestamp)
            .ToListAsync();
    }
}