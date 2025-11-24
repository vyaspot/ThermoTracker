using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Enums;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public class FileLoggingService : IFileLoggingService, IDisposable
{
    private readonly FileLoggingSettings _settings;
    private readonly ILogger<FileLoggingService> _logger;
    private readonly object _fileLock = new();
    private string _currentLogFilePath;

    // Column width: consistent formatting
    private const int TimestampWidth = 19;
    private const int SensorNameWidth = 25;
    private const int LocationWidth = 16;
    private const int TemperatureWidth = 11;
    private const int StatusWidth = 8;
    private const int AlertTypeWidth = 11;

    public FileLoggingService(IOptions<FileLoggingSettings> settings, ILogger<FileLoggingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _currentLogFilePath = GetCurrentLogFilePath();

        Directory.CreateDirectory(_settings.LogDirectory);
        InitializeLogFile();

        _logger.LogInformation("File logging service initialized. Log directory: {LogDirectory}",
            Path.GetFullPath(_settings.LogDirectory));
    }

    private void InitializeLogFile()
    {
        try
        {
            var fileInfo = new FileInfo(_currentLogFilePath);
            if (_settings.IncludeHeader && (!fileInfo.Exists || fileInfo.Length == 0))
            {
                WriteHeader();
                _logger.LogInformation("Header written to new log file: {FilePath}", _currentLogFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize log file");
        }
    }

    private void WriteHeader()
    {
        string header;

        if (_settings.UseHumanReadableFormat)
        {
            // Updated header with increased sensor name width
            header = "Timestamp           | Sensor Name                 | Location         | Temperature | Status   | Alert Type";
        }
        else
        {
            header = "Timestamp\tSensorName\tLocation\tTemperature\tStatus\tAlertType";
        }

        lock (_fileLock)
        {
            using var writer = new StreamWriter(_currentLogFilePath, false, Encoding.UTF8);
            writer.WriteLine(header);

            if (_settings.UseHumanReadableFormat)
            {
                // Updated separator line to match new width (110 characters)
                writer.WriteLine(new string('-', 110));
            }
        }
    }

    public async Task LogSensorReadingAsync(SensorData data)
    {
        try
        {
            var newFilePath = GetCurrentLogFilePath();
            if (newFilePath != _currentLogFilePath)
            {
                await RotateToNewDayAsync(newFilePath);
            }

            await EnsureFileSizeWithinLimitAsync();

            var logLine = FormatLogEntry(data);

            lock (_fileLock)
            {
                using var writer = new StreamWriter(_currentLogFilePath, true, Encoding.UTF8);
                writer.WriteLine(logLine);
            }

            _logger.LogDebug("Successfully logged sensor data to file: {SensorName}", data.SensorName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log sensor reading to file for sensor {SensorName}",
                data.SensorName);
        }
    }

    private string FormatLogEntry(SensorData data)
    {
        if (_settings.UseHumanReadableFormat)
        {
            return FormatHumanReadableEntry(data);
        }
        else
        {
            return FormatTabSeparatedEntry(data);
        }
    }

    private string FormatHumanReadableEntry(SensorData data)
    {
        var timestamp = data.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        // Updated column widths with increased sensor name width
        var sensorName = PadField(data.SensorName, SensorNameWidth);
        var location = PadField(data.SensorLocation, LocationWidth);
        var temperature = PadField($"{data.Temperature:0.00}°C", TemperatureWidth);
        var status = PadField(GetStatusDisplay(data), StatusWidth);
        var alertType = PadField(GetAlertTypeDisplay(data.AlertType), AlertTypeWidth);

        return $"{timestamp} | {sensorName}   | {location} | {temperature} | {status} | {alertType}";
    }

    private string FormatTabSeparatedEntry(SensorData data)
    {
        var timestamp = data.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        var sensorName = EscapeField(data.SensorName);
        var location = EscapeField(data.SensorLocation);
        var status = GetStatusDisplay(data).Trim();
        var alertType = data.AlertType.ToString();

        return $"{timestamp}\t{sensorName}\t{location}\t{data.Temperature:0.00}\t{status}\t{alertType}";
    }

    private static string PadField(string field, int length)
    {
        if (string.IsNullOrEmpty(field))
            return new string(' ', length);

        if (field.Length > length)
            return field.Substring(0, length - 3) + "...";

        return field.PadRight(length);
    }

    private string GetStatusDisplay(SensorData data)
    {
        if (data.IsFaulty) return "FAULTY  ";
        if (data.IsSpike) return "SPIKE   ";
        if (!data.IsValid) return "INVALID ";
        if (data.IsAnomaly) return "ANOMALY ";
        return "VALID   ";
    }

    private static string GetAlertTypeDisplay(AlertType alertType)
    {
        return alertType switch
        {
            AlertType.None => "NORMAL    ",
            AlertType.Threshold => "THRESHOLD  ",
            AlertType.Anomaly => "ANOMALY    ",
            AlertType.Spike => "SPIKE      ",
            AlertType.Fault => "FAULT      ",
            AlertType.Offline => "OFFLINE    ",
            _ => "UNKNOWN    "
        };
    }

    private static string EscapeField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        return field.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }

    private async Task RotateToNewDayAsync(string newFilePath)
    {
        lock (_fileLock)
        {
            _currentLogFilePath = newFilePath;
            InitializeLogFile();
            _logger.LogInformation("Rotated to new daily log file: {NewFile}", _currentLogFilePath);
        }
    }

    private async Task EnsureFileSizeWithinLimitAsync()
    {
        if (!_settings.EnableRotation) return;

        try
        {
            var fileInfo = new FileInfo(_currentLogFilePath);
            if (fileInfo.Exists && fileInfo.Length > _settings.MaxFileSizeMB * 1024 * 1024)
            {
                await RotateLogFileAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check log file size for rotation");
        }
    }

    private async Task RotateLogFileAsync()
    {
        lock (_fileLock)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var rotatedFilePath = _currentLogFilePath.Replace(".txt", $"_{timestamp}.txt");

            if (File.Exists(_currentLogFilePath))
            {
                File.Move(_currentLogFilePath, rotatedFilePath);
            }

            _currentLogFilePath = GetCurrentLogFilePath();
            InitializeLogFile();
            _logger.LogInformation("Log file rotated: {NewFile}", rotatedFilePath);
        }

        await CleanOldLogFilesAsync();
    }

    private string GetCurrentLogFilePath()
    {
        var fileName = _settings.LogFileName.Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
        return Path.Combine(_settings.LogDirectory, fileName);
    }

    public async Task CleanOldLogFilesAsync()
    {
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-_settings.RetentionDays);
            var logFiles = Directory.GetFiles(_settings.LogDirectory, "*.txt");

            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    File.Delete(file);
                    _logger.LogInformation("Deleted old log file: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean old log files");
        }
    }

    public Task<string> GetCurrentLogFilePathAsync()
    {
        return Task.FromResult(Path.GetFullPath(_currentLogFilePath));
    }

    public Task<IEnumerable<string>> GetLogFilesAsync()
    {
        var files = Directory.Exists(_settings.LogDirectory)
            ? Directory.GetFiles(_settings.LogDirectory, "*.txt")
                .OrderByDescending(f => new FileInfo(f).CreationTime)
                .ToArray()
            : Array.Empty<string>();

        return Task.FromResult(files.AsEnumerable());
    }

    public Task<long> GetCurrentLogFileSizeAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_currentLogFilePath);
            return Task.FromResult(fileInfo.Exists ? fileInfo.Length : 0);
        }
        catch
        {
            return Task.FromResult(0L);
        }
    }

    public Task<int> GetCurrentLogFileEntryCountAsync()
    {
        try
        {
            if (!File.Exists(_currentLogFilePath))
                return Task.FromResult(0);

            var lines = File.ReadAllLines(_currentLogFilePath);
            var subtractLines = _settings.IncludeHeader ? 1 : 0;
            if (_settings.UseHumanReadableFormat && _settings.IncludeHeader)
            {
                subtractLines++;
            }

            var count = Math.Max(0, lines.Length - subtractLines);
            return Task.FromResult(count);
        }
        catch
        {
            return Task.FromResult(0);
        }
    }

    public void Dispose()
    {
    }
}