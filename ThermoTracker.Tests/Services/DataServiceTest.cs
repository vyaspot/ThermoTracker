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

        var sensorData = new SensorData
        {
            SensorName = "TempSensor1",
            SensorLocation = "Room1",
            Temperature = 23.456M,
            SmoothedValue = 23.444M,
            IsValid = true,
            Timestamp = DateTime.UtcNow
        };

        await service.StoreDataAsync(sensorData);

        _fileLoggingServiceMock.Verify(x => x.LogSensorReadingAsync(sensorData), Times.Once);
        Assert.Equal(23.46M, sensorData.Temperature);
        Assert.Equal(23.44M, sensorData.SmoothedValue);

        var saved = await context.SensorData.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Equal("TempSensor1", saved.SensorName);
    }

    [Fact]
    public async Task GetRecentDataAsync_ExistingSensor_ReturnsCorrectData()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        await SeedSensorDataAsync(context,
            new SensorData { Id = 1, SensorId = 1, SensorName = "TempSensor1", Temperature = 23.5M, Timestamp = DateTime.UtcNow.AddMinutes(-5) },
            new SensorData { Id = 2, SensorId = 1, SensorName = "TempSensor1", Temperature = 23.6M, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
            new SensorData { Id = 3, SensorId = 2, SensorName = "OtherSensor", Temperature = 22.5M, Timestamp = DateTime.UtcNow.AddMinutes(-3) }
        );

        var result = await service.GetRecentDataAsync(1, 5);

        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Equal("TempSensor1", x.SensorName));
    }

    [Fact]
    public async Task GetRecentDataAsync_NonExistentSensor_ReturnsEmpty()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        var result = await service.GetRecentDataAsync(999, 5);
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

        var data = new SensorData { SensorName = "TempSensor1", SensorLocation = "Room1", Temperature = 23.5M, IsValid = true, AlertType = type };
        await service.LogDataAsync(data);

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

        var olderThan = TimeSpan.FromDays(30);
        var cutoff = DateTime.UtcNow - olderThan;

        await SeedSensorDataAsync(context,
            new SensorData { Timestamp = cutoff.AddDays(-1) },
            new SensorData { Timestamp = cutoff.AddDays(1) }
        );

        var removed = await service.CleanOldDataAsync(olderThan);
        Assert.Equal(1, removed);
        Assert.Single(context.SensorData);
    }

    [Fact]
    public async Task GetSensorStatisticsAsync_CalculatesCorrectValues()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        const string sensorName = "TempSensor1";

        await SeedSensorDataAsync(context,
            new SensorData { SensorName = sensorName, Temperature = 23.0M, IsValid = true },
            new SensorData { SensorName = sensorName, Temperature = 25.0M, IsValid = true, IsAnomaly = true },
            new SensorData { SensorName = sensorName, Temperature = 24.0M, IsValid = false, IsSpike = true }
        );

        var stats = await service.GetSensorStatisticsAsync(sensorName, TimeSpan.FromHours(24));
        Assert.Equal(sensorName, stats.SensorName);
        Assert.Equal(3, stats.TotalReadings);
        Assert.Equal(2, stats.ValidReadings);
        Assert.Equal(1, stats.AnomalyReadings);
        Assert.Equal(1, stats.SpikeReadings);
        Assert.Equal(24.0M, stats.AverageTemperature);
    }

    [Fact]
    public async Task GetFileLoggingInfoAsync_ReturnsCorrectData()
    {
        using var context = CreateDbContext();
        var service = CreateService(context);

        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFilePathAsync()).ReturnsAsync("Logs/log.txt");
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFileSizeAsync()).ReturnsAsync(1024);
        _fileLoggingServiceMock.Setup(x => x.GetCurrentLogFileEntryCountAsync()).ReturnsAsync(5);
        _fileLoggingServiceMock.Setup(x => x.GetLogFilesAsync()).ReturnsAsync(new List<string> { "f1.txt", "f2.txt" });

        var info = await service.GetFileLoggingInfoAsync();

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

        await service.CleanupLogsAsync();
        _fileLoggingServiceMock.Verify(x => x.CleanOldLogFilesAsync(), Times.Once);
    }
}