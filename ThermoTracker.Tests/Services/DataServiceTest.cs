using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Data;
using ThermoTracker.ThermoTracker.Enums;
using ThermoTracker.ThermoTracker.Models;
using ThermoTracker.ThermoTracker.Services;

namespace ThermoTracker.ThermoTracker.Tests.Services;

public class DataServiceTests
{
    private readonly Mock<IFileLoggingService> _fileLoggingServiceMock;
    private readonly Mock<IOptions<FileLoggingSettings>> _fileLoggingSettingsMock;
    private readonly Mock<ILogger<DataService>> _loggerMock;
    private readonly DataService _dataService;
    private readonly FileLoggingSettings _fileLoggingSettings;
    private readonly DbContextOptions<SensorDbContext> _dbContextOptions;

    public DataServiceTests()
    {
        _fileLoggingServiceMock = new Mock<IFileLoggingService>();
        _fileLoggingSettingsMock = new Mock<IOptions<FileLoggingSettings>>();
        _loggerMock = new Mock<ILogger<DataService>>();

        _fileLoggingSettings = new FileLoggingSettings
        {
            LogDirectory = "Logs",
            LogFileName = "Sensor_Readings_{date}.txt",
            MaxFileSizeMB = 10,
            RetentionDays = 7,
            EnableRotation = true,
            IncludeHeader = true,
            TimestampFormat = "yyyy-MM-dd HH:mm:ss",
            UseHumanReadableFormat = true
        };

        _fileLoggingSettingsMock.Setup(x => x.Value).Returns(_fileLoggingSettings);

        // Use in-memory database for testing
        _dbContextOptions = new DbContextOptionsBuilder<SensorDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDatabase_{Guid.NewGuid()}")
            .Options;


        var context = new SensorDbContext(_dbContextOptions);
        _dataService = new DataService(
            context,
            _fileLoggingServiceMock.Object,
            _fileLoggingSettingsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task StoreDataAsync_WithValidData_StoresAndLogsSuccessfully()
    {
        // Arrange
        var sensorData = new SensorData
        {
            SensorName = "TempSensor1",
            SensorLocation = "Room1",
            Temperature = 23.456M,
            SmoothedValue = 23.444M,
            IsValid = true,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _dataService.StoreDataAsync(sensorData);

        // Assert
        _fileLoggingServiceMock.Verify(x => x.LogSensorReadingAsync(sensorData), Times.Once);

        // Verify temperature rounding
        Assert.Equal(23.46M, sensorData.Temperature);
        Assert.Equal(23.44M, sensorData.SmoothedValue);

        // Verify data was saved to database
        using var context = new SensorDbContext(_dbContextOptions);
        var savedData = await context.SensorData.FirstOrDefaultAsync();
        Assert.NotNull(savedData);
        Assert.Equal("TempSensor1", savedData.SensorName);
    }

    [Fact]
    public async Task GetRecentDataAsync_WithValidSensorName_ReturnsRecentData()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var testData = new List<SensorData>
            {
                new SensorData { Id = 1, SensorName = "TempSensor1", Temperature = 23.5M, Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new SensorData { Id = 2, SensorName = "TempSensor1", Temperature = 23.6M, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
                new SensorData { Id = 3, SensorName = "OtherSensor", Temperature = 22.5M, Timestamp = DateTime.UtcNow.AddMinutes(-3) }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetRecentDataAsync(1, 5);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Equal("TempSensor1", x.SensorName));
    }

    [Fact]
    public async Task GetRecentDataAsync_WithNonExistentSensor_ReturnsEmptyList()
    {
        // Act
        var result = await _dataService.GetRecentDataAsync(5, 5);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(AlertType.None, LogLevel.Information)]
    [InlineData(AlertType.Fault, LogLevel.Error)]
    [InlineData(AlertType.Spike, LogLevel.Warning)]
    [InlineData(AlertType.Threshold, LogLevel.Warning)]
    [InlineData(AlertType.Anomaly, LogLevel.Warning)]
    public async Task LogDataAsync_WithDifferentAlertTypes_LogsAppropriateLevel(AlertType alertType, LogLevel expectedLogLevel)
    {
        // Arrange
        var sensorData = new SensorData
        {
            Id = 1,
            SensorName = "TempSensor1",
            SensorLocation = "Room1",
            Temperature = 23.5M,
            IsValid = true,
            AlertType = alertType
        };

        // Act
        await _dataService.LogDataAsync(sensorData);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                expectedLogLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"Sensor {sensorData.SensorName}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task GetDataHistoryAsync_WithValidParameters_ReturnsFilteredData()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var sensorName = "TempSensor1";
        var duration = TimeSpan.FromHours(1);
        var startTime = DateTime.UtcNow - duration;

        var testData = new List<SensorData>
            {
                new SensorData { SensorName = sensorName, Temperature = 23.5M, Timestamp = startTime.AddMinutes(10) },
                new SensorData { SensorName = sensorName, Temperature = 23.6M, Timestamp = startTime.AddMinutes(20) },
                new SensorData { SensorName = sensorName, Temperature = 23.7M, Timestamp = startTime.AddMinutes(-10) } // Too old
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetDataHistoryAsync(sensorName, duration);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.True(x.Timestamp >= startTime));
    }

    [Fact]
    public async Task GetAnomaliesAsync_ReturnsOnlyAnomaliesAndAlerts()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var testData = new List<SensorData>
            {
                new() { SensorName = "Sensor1", IsAnomaly = true, AlertType = AlertType.None },
                new() { SensorName = "Sensor2", IsAnomaly = false, AlertType = AlertType.Spike },
                new() { SensorName = "Sensor3", IsAnomaly = false, AlertType = AlertType.None }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetAnomaliesAsync(TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.True(x.IsAnomaly || x.AlertType != AlertType.None));
    }

    [Fact]
    public async Task GetSensorDataByDateRangeAsync_WithValidRange_ReturnsFilteredData()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var sensorName = "TempSensor1";
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow.AddHours(-1);

        var testData = new List<SensorData>
            {
                new() { SensorName = sensorName, Timestamp = start.AddMinutes(15) },
                new() { SensorName = sensorName, Timestamp = start.AddMinutes(45) },
                new() { SensorName = sensorName, Timestamp = end.AddMinutes(15) } // Outside range
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetSensorDataByDateRangeAsync(sensorName, start, end);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.True(x.Timestamp >= start && x.Timestamp <= end));
    }

    [Fact]
    public async Task CleanOldDataAsync_WithOldData_RemovesRecordsAndReturnsCount()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var olderThan = TimeSpan.FromDays(30);
        var cutoffTime = DateTime.UtcNow - olderThan;

        var testData = new List<SensorData>
            {
                new() { Timestamp = cutoffTime.AddDays(-1) },
                new() { Timestamp = cutoffTime.AddDays(1) }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.CleanOldDataAsync(olderThan);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetSensorStatisticsAsync_WithData_ReturnsCorrectStatistics()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var sensorName = "TempSensor1";

        var testData = new List<SensorData>
            {
                new() { SensorName = sensorName, Temperature = 23.0M, IsValid = true, IsAnomaly = false, IsSpike = false, IsFaulty = false },
                new() { SensorName = sensorName, Temperature = 24.0M, IsValid = true, IsAnomaly = true, IsSpike = false, IsFaulty = false },
                new() { SensorName = sensorName, Temperature = 25.0M, IsValid = false, IsAnomaly = false, IsSpike = true, IsFaulty = false },
                new() { SensorName = sensorName, Temperature = 26.0M, IsValid = true, IsAnomaly = false, IsSpike = false, IsFaulty = true }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetSensorStatisticsAsync(sensorName, TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(sensorName, result.SensorName);
        Assert.Equal(4, result.TotalReadings);
        Assert.Equal(3, result.ValidReadings);
        Assert.Equal(1, result.AnomalyReadings);
        Assert.Equal(1, result.SpikeReadings);
        Assert.Equal(1, result.FaultReadings);
        Assert.Equal(24.5M, result.AverageTemperature);
    }

    [Fact]
    public async Task GetOverallStatisticsAsync_WithMultipleSensors_ReturnsAggregateStatistics()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var testData = new List<SensorData>
            {
                new() { SensorName = "Sensor1", Temperature = 23.0M, IsAnomaly = true, IsSpike = false, IsFaulty = false },
                new() { SensorName = "Sensor1", Temperature = 24.0M, IsAnomaly = false, IsSpike = true, IsFaulty = false },
                new() { SensorName = "Sensor2", Temperature = 22.0M, IsAnomaly = false, IsSpike = false, IsFaulty = true },
                new() { SensorName = "Sensor3", Temperature = 21.0M, IsAnomaly = false, IsSpike = false, IsFaulty = false }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetOverallStatisticsAsync(TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(3, result.TotalSensors);
        Assert.Equal(4, result.TotalReadings);
        Assert.Equal(1, result.TotalAnomalies);
        Assert.Equal(1, result.TotalSpikes);
        Assert.Equal(1, result.TotalFaults);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_ReturnsAlertData()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var testData = new List<SensorData>
            {
                new() {
                    SensorName = "Sensor1",
                    SensorLocation = "Room1",
                    Temperature = 23.5M,
                    AlertType = AlertType.Spike,
                    Timestamp = DateTime.UtcNow.AddMinutes(-5)
                },
                new() {
                    SensorName = "Sensor2",
                    SensorLocation = "Room2",
                    Temperature = 45.0M,
                    AlertType = AlertType.Threshold,
                    Timestamp = DateTime.UtcNow.AddMinutes(-10)
                },
                new() {
                    SensorName = "Sensor3",
                    SensorLocation = "Room3",
                    Temperature = 22.5M,
                    AlertType = AlertType.None
                }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetRecentAlertsAsync(5);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.NotEqual(AlertType.None, x.AlertType));
    }

    [Fact]
    public async Task GetFaultyReadingsAsync_ReturnsFaultyAndSpikeData()
    {
        // Arrange
        using var context = new SensorDbContext(_dbContextOptions);
        var testData = new List<SensorData>
            {
                new() { IsFaulty = true, IsSpike = false },
                new() { IsFaulty = false, IsSpike = true },
                new() { IsFaulty = false, IsSpike = false }
            };

        await context.SensorData.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        // Act
        var result = await _dataService.GetFaultyReadingsAsync(TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.True(x.IsFaulty || x.IsSpike));
    }

    [Fact]
    public async Task GetFileLoggingInfoAsync_ReturnsCorrectInformation()
    {
        // Arrange
        var expectedPath = "Logs/Sensor_Readings_20240101.txt";
        var expectedSize = 1024L;
        var expectedCount = 42;
        var expectedFiles = new List<string> { "file1.txt", "file2.txt" };

        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFilePathAsync()).ReturnsAsync(expectedPath);
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFileSizeAsync()).ReturnsAsync(expectedSize);
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFileEntryCountAsync()).ReturnsAsync(expectedCount);
        _fileLoggingServiceMock.Setup(x => x.GetLogFilesAsync()).ReturnsAsync(expectedFiles);

        // Act
        var result = await _dataService.GetFileLoggingInfoAsync();

        // Assert
        Assert.Equal(expectedPath, result.CurrentLogFilePath);
        Assert.Equal(expectedSize, result.CurrentLogFileSizeBytes);
        Assert.Equal(expectedCount, result.CurrentFileEntryCount);
        Assert.Equal(expectedFiles, result.LogFiles);
    }

    [Fact]
    public async Task CleanupLogsAsync_CallsFileLoggingService()
    {
        // Act
        await _dataService.CleanupLogsAsync();

        // Assert
        _fileLoggingServiceMock.Verify(x => x.CleanOldLogFilesAsync(), Times.Once);
    }

    [Fact]
    public async Task LogDataAsync_WithInvalidData_LogsInvalidStatus()
    {
        // Arrange
        var sensorData = new SensorData
        {
            SensorName = "TempSensor1",
            SensorLocation = "Room1",
            Temperature = 23.5M,
            IsValid = false,
            AlertType = AlertType.None
        };

        // Act
        await _dataService.LogDataAsync(sensorData);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("INVALID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task GetSensorStatisticsAsync_WithNoData_ReturnsZeroStatistics()
    {
        // Act
        var result = await _dataService.GetSensorStatisticsAsync("NonExistentSensor", TimeSpan.FromHours(24));

        // Assert
        Assert.Equal("NonExistentSensor", result.SensorName);
        Assert.Equal(0, result.TotalReadings);
        Assert.Equal(0, result.ValidReadings);
        Assert.Equal(0, result.AverageTemperature);
    }
}