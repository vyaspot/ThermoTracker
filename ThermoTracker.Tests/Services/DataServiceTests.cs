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
    private readonly Mock<IFileLoggingService> _fileLoggingServiceMock = new();
    private readonly Mock<IOptions<FileLoggingSettings>> _fileLoggingSettingsMock = new();
    private readonly Mock<ILogger<DataService>> _loggerMock = new();
    private readonly FileLoggingSettings _fileLoggingSettings;

    public DataServiceTests()
    {
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
    }

    private SensorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SensorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SensorDbContext(options);
    }

    private DataService CreateService(SensorDbContext context)
    {
        return new DataService(context, _fileLoggingServiceMock.Object, _fileLoggingSettingsMock.Object, _loggerMock.Object);
    }

    private async Task<List<SensorData>> SeedSensorDataAsync(SensorDbContext context, params SensorData[] data)
    {
        await context.SensorData.AddRangeAsync(data);
        await context.SaveChangesAsync();
        return data.ToList();
    }

    [Fact]
    public async Task StoreDataAsync_ValidData_StoresAndLogs()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

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
        await service.StoreDataAsync(sensorData);

        // Assert
        _fileLoggingServiceMock.Verify(x => x.LogSensorReadingAsync(sensorData), Times.AtLeastOnce);
        Assert.Equal(23.46M, sensorData.Temperature);
        Assert.Equal(23.44M, sensorData.SmoothedValue);

        var saved = await context.SensorData.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Equal("TempSensor1", saved.SensorName);
    }

    [Fact]
    public async Task StoreDataAsync_NullData_ThrowsArgumentNullException()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.StoreDataAsync(null!));
    }

    [Fact]
    public async Task GetRecentDataAsync_ExistingSensor_ReturnsCorrectData()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        await SeedSensorDataAsync(context,
            new SensorData { SensorId = 1, SensorName = "TempSensor1", Temperature = 23.5M, Timestamp = now.AddMinutes(-5) },
            new SensorData { SensorId = 1, SensorName = "TempSensor1", Temperature = 23.6M, Timestamp = now.AddMinutes(-10) },
            new SensorData { SensorId = 2, SensorName = "OtherSensor", Temperature = 22.5M, Timestamp = now.AddMinutes(-3) }
        );

        // Act
        var result = await service.GetRecentDataAsync(1, 5);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Equal("TempSensor1", x.SensorName));
    }

    [Fact]
    public async Task GetRecentDataAsync_NonExistentSensor_ReturnsEmpty()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Act
        var result = await service.GetRecentDataAsync(999, 5);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(AlertType.None, LogLevel.Information)]
    [InlineData(AlertType.Fault, LogLevel.Error)]
    [InlineData(AlertType.Spike, LogLevel.Warning)]
    [InlineData(AlertType.Threshold, LogLevel.Warning)]
    [InlineData(AlertType.Anomaly, LogLevel.Warning)]
    public async Task LogDataAsync_AlertType_LogsCorrectLevel(AlertType type, LogLevel expectedLevel)
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var data = new SensorData
        {
            SensorName = "TempSensor1",
            SensorLocation = "Room1",
            Temperature = 23.5M,
            IsValid = true,
            AlertType = type
        };

        // Act
        await service.LogDataAsync(data);

        // Assert
        _loggerMock.Verify(x =>
            x.Log(
                expectedLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(data.SensorName)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task CleanOldDataAsync_RemovesOldRecords()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        var olderThan = TimeSpan.FromDays(30);
        var cutoff = now - olderThan;
        await SeedSensorDataAsync(context,
            new SensorData { Timestamp = cutoff.AddDays(-1) },
            new SensorData { Timestamp = cutoff.AddDays(1) }
        );

        // Act
        var removed = await service.CleanOldDataAsync(olderThan);

        // Assert
        Assert.Equal(1, removed);
        Assert.Single(context.SensorData);
    }

    [Fact]
    public async Task CleanOldDataAsync_NoOldRecords_ReturnsZero()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        await SeedSensorDataAsync(context,
            new SensorData { Timestamp = DateTime.UtcNow }
        );

        // Act
        var removed = await service.CleanOldDataAsync(TimeSpan.FromDays(30));

        // Assert
        Assert.Equal(0, removed);
        Assert.Single(context.SensorData);
    }

    [Fact]
    public async Task GetSensorStatisticsAsync_CalculatesCorrectValues()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        const string sensorName = "TempSensor1";
        await SeedSensorDataAsync(context,
            new SensorData { SensorName = sensorName, Temperature = 23.0M, IsValid = true },
            new SensorData { SensorName = sensorName, Temperature = 25.0M, IsValid = true, IsAnomaly = true },
            new SensorData { SensorName = sensorName, Temperature = 24.0M, IsValid = false, IsSpike = true }
        );

        // Act
        var stats = await service.GetSensorStatisticsAsync(sensorName, TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(sensorName, stats.SensorName);
        Assert.Equal(3, stats.TotalReadings);
        Assert.Equal(2, stats.ValidReadings);
        Assert.Equal(1, stats.AnomalyReadings);
        Assert.Equal(1, stats.SpikeReadings);
        Assert.Equal(24.0M, stats.AverageTemperature);
    }

    [Fact]
    public async Task GetDataHistoryAsync_ReturnsCorrectData()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        await SeedSensorDataAsync(context,
            new SensorData { SensorName = "S1", Timestamp = now.AddHours(-1) },
            new SensorData { SensorName = "S1", Timestamp = now.AddHours(-5) },
            new SensorData { SensorName = "S2", Timestamp = now.AddHours(-2) }
        );

        // Act
        var data = await service.GetDataHistoryAsync("S1", TimeSpan.FromHours(3));

        // Assert
        Assert.Single(data);
        Assert.All(data, x => Assert.Equal("S1", x.SensorName));
    }

    [Fact]
    public async Task GetAnomaliesAsync_ReturnsOnlyAnomalies()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        await SeedSensorDataAsync(context,
            new SensorData { SensorName = "S1", IsAnomaly = true, Timestamp = now },
            new SensorData { SensorName = "S1", IsAnomaly = false, Timestamp = now }
        );

        // Act
        var anomalies = await service.GetAnomaliesAsync(TimeSpan.FromDays(1));

        // Assert
        Assert.Single(anomalies);
        Assert.True(anomalies.First().IsAnomaly);
    }

    [Fact]
    public async Task GetSensorDataByDateRangeAsync_ReturnsCorrectSubset()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        await SeedSensorDataAsync(context,
            new SensorData { SensorName = "S1", Timestamp = now.AddDays(-2) },
            new SensorData { SensorName = "S1", Timestamp = now.AddDays(-1) },
            new SensorData { SensorName = "S1", Timestamp = now }
        );

        // Act
        var subset = await service.GetSensorDataByDateRangeAsync("S1", now.AddDays(-1.5), now);

        // Assert
        Assert.Equal(2, subset.Count);
    }

    [Fact]
    public async Task GetOverallStatisticsAsync_ReturnsCorrectTotals()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        await SeedSensorDataAsync(context,
            new SensorData { SensorName = "S1", Temperature = 20, IsValid = true, Timestamp = now },
            new SensorData { SensorName = "S2", Temperature = 22, IsValid = true, IsAnomaly = true, Timestamp = now }
        );

        // Act
        var overall = await service.GetOverallStatisticsAsync(TimeSpan.FromDays(1));

        // Assert
        Assert.Equal(2, overall.TotalReadings);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_ReturnsMostRecentAlerts()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;
        await SeedSensorDataAsync(context,
            new SensorData { SensorName = "S1", AlertType = AlertType.Fault, Timestamp = now },
            new SensorData { SensorName = "S1", AlertType = AlertType.Spike, Timestamp = now }
        );

        // Act
        var alerts = await service.GetRecentAlertsAsync(1);

        // Assert
        Assert.Single(alerts);
        Assert.Contains(alerts.First().AlertType, new[] { AlertType.Fault, AlertType.Spike });
    }

    [Fact]
    public async Task GetFaultyReadingsAsync_ReturnsOnlyInvalid()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        var now = DateTime.UtcNow;

        await SeedSensorDataAsync(context,
            new SensorData
            {
                SensorId = 1,
                SensorName = "S1",
                IsValid = false,
                Timestamp = now,
                Temperature = 20M,
                SmoothedValue = 20M,
                IsFaulty = true,
                AlertType = AlertType.Fault,
                IsAnomaly = false,
                IsSpike = false
            },
            new SensorData
            {
                SensorId = 2,
                SensorName = "S2",
                IsValid = true,
                Timestamp = now,
                Temperature = 23M,
                SmoothedValue = 23M,
                IsFaulty = false,
                AlertType = AlertType.None
            }
        );

        // Act
        var faulty = await service.GetFaultyReadingsAsync(TimeSpan.FromDays(1));

        // Assert
        Assert.Single(faulty);
        Assert.False(faulty.First().IsValid);
        Assert.Equal("S1", faulty.First().SensorName);
    }


    [Fact]
    public async Task GetFileLoggingInfoAsync_ReturnsCorrectData()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Arrange
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFilePathAsync()).ReturnsAsync("Logs/log.txt");
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFileSizeAsync()).ReturnsAsync(1024);
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFileEntryCountAsync()).ReturnsAsync(5);
        _fileLoggingServiceMock.Setup(x => x.GetLogFilesAsync()).ReturnsAsync(new List<string> { "f1.txt", "f2.txt" });

        // Act
        var info = await service.GetFileLoggingInfoAsync();

        // Assert
        Assert.Equal("Logs/log.txt", info.CurrentLogFilePath);
        Assert.Equal(1024, info.CurrentLogFileSizeBytes);
        Assert.Equal(5, info.CurrentFileEntryCount);
        Assert.Equal(2, info.LogFiles.Count);
    }

    [Fact]
    public async Task CleanupLogsAsync_CallsServiceMethod()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        // Act
        await service.CleanupLogsAsync();

        // Assert
        _fileLoggingServiceMock.Verify(x => x.CleanOldLogFilesAsync(), Times.Once);
    }
}