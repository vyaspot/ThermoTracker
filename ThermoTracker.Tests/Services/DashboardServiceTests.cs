using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Data;
using ThermoTracker.ThermoTracker.Models;
using ThermoTracker.ThermoTracker.Services;


namespace ThermoTracker.ThermoTracker.Tests.Services;

public class DashboardServiceTests : IDisposable
{
    private readonly Mock<ISensorService> _sensorServiceMock = new();
    private readonly Mock<IDataService> _dataServiceMock = new();
    private readonly Mock<IOptions<FileLoggingSettings>> _fileLoggingOptionsMock = new();
    private readonly Mock<IOptions<SimulationSettings>> _simulationOptionsMock = new();
    private readonly Mock<IOptions<TemperatureRangeSettings>> _fixedRangeOptionsMock = new();
    private readonly Mock<ILogger<DashboardService>> _loggerMock = new();
    private readonly Mock<ISensorConfigWatcher> _configWatcherMock = new();


    private readonly FileLoggingSettings _fileLoggingSettings;
    private readonly SimulationSettings _simulationSettings;
    private readonly TemperatureRangeSettings _temperatureRangeSettings;
    private readonly DbContextOptions<SensorDbContext> _dbContextOptions;
    private readonly SensorDbContext _context;
    private readonly CancellationTokenSource _cts = new();

    public DashboardServiceTests()
    {
        _fileLoggingSettings = new FileLoggingSettings();
        _simulationSettings = new SimulationSettings { UpdateIntervalMs = 100, DataHistorySize = 50, MovingAverageWindow = 10 };
        _temperatureRangeSettings = new TemperatureRangeSettings();

        _fileLoggingOptionsMock.Setup(x => x.Value).Returns(_fileLoggingSettings);
        _simulationOptionsMock.Setup(x => x.Value).Returns(_simulationSettings);
        _fixedRangeOptionsMock.Setup(x => x.Value).Returns(_temperatureRangeSettings);

        _dbContextOptions = new DbContextOptionsBuilder<SensorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new SensorDbContext(_dbContextOptions);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _cts?.Dispose();
    }

    private DashboardService CreateService(bool disableTimer = true)
    {
        var service = new DashboardService(
            _sensorServiceMock.Object,
            _dataServiceMock.Object,
            _configWatcherMock.Object,
            _fileLoggingOptionsMock.Object,
            _simulationOptionsMock.Object,
            _fixedRangeOptionsMock.Object,
            _loggerMock.Object);

        if (disableTimer)
        {
            typeof(DashboardService)
                .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(service, null);
        }

        return service;
    }

    private void InitializeState(DashboardService service, IEnumerable<Sensor> sensors)
    {
        var sensorsField = typeof(DashboardService)
            .GetField("_sensors", BindingFlags.NonPublic | BindingFlags.Instance);
        sensorsField?.SetValue(service, sensors.ToList());

        var dataHistoryField = typeof(DashboardService)
            .GetField("_dataHistory", BindingFlags.NonPublic | BindingFlags.Instance);

        var history = sensors.ToDictionary(s => s.Name, _ => new List<SensorData>());
        dataHistoryField?.SetValue(service, history);
    }

    private object? InvokePrivateMethod(DashboardService service, string methodName, params object?[]? parameters)
    {
        parameters ??= Array.Empty<object>();

        var method = typeof(DashboardService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == parameters.Length)
            ?? throw new InvalidOperationException($"Method {methodName} not found with {parameters.Length} parameters.");

        return method.Invoke(service, parameters);
    }

    private static string InvokeStaticPrivateMethod(Type type, string methodName, params object?[] parameters)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(null, parameters) as string ?? string.Empty;
    }

    [Fact]
    public async Task StartAsync_ValidSensors_InitializesSuccessfully()
    {
        // Arrange
        var sensors = new[]
        {
                new Sensor { Id = 1, Name = "S1", Location = "R1", Status = "ACTIVE" },
                new Sensor { Id = 2, Name = "S2", Location = "R2", Status = "ACTIVE" }
            };
        _sensorServiceMock.Setup(x => x.GetSensors()).Returns(sensors.ToList());
        var service = CreateService();

        // Act
        await service.StartAsync(_cts.Token);

        // Assert
        _sensorServiceMock.Verify(x => x.GetSensors(), Times.Once);
        _loggerMock.VerifyLogContains("Dashboard service started with 2 sensors");
        _loggerMock.VerifyLogContains("Fixed temperature validation range:");
    }

    [Fact]
    public async Task StartAsync_SensorServiceThrows_LogsErrorAndThrows()
    {
        // Arrange
        _sensorServiceMock.Setup(x => x.GetSensors()).Throws(new InvalidOperationException("Fail"));
        var service = CreateService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(_cts.Token));
        Assert.Equal("Fail", ex.Message);
        _loggerMock.VerifyLogContains("Failed to start dashboard service", LogLevel.Error);
    }

    [Fact]
    public async Task StopAsync_DisposesTimerAndLogs()
    {
        // Arrange
        var sensors = new[] { new Sensor { Name = "S1" } };
        _sensorServiceMock.Setup(x => x.GetSensors()).Returns(sensors.ToList());
        var service = CreateService(disableTimer: false);
        await service.StartAsync(_cts.Token);

        // Act
        await service.StopAsync(_cts.Token);

        // Assert
        _loggerMock.VerifyLogContains("Dashboard service stopped");
    }

    [Fact]
    public void UpdateDashboard_TemperatureStyle_ReturnsCorrectColors()
    {
        // Arrange / Act / Assert all in one
        Assert.Equal("blue", InvokeStaticPrivateMethod(typeof(DashboardService), "GetTemperatureStyle", 18.0M));
        Assert.Equal("cyan", InvokeStaticPrivateMethod(typeof(DashboardService), "GetTemperatureStyle", 21.0M));
        Assert.Equal("green", InvokeStaticPrivateMethod(typeof(DashboardService), "GetTemperatureStyle", 23.0M));
        Assert.Equal("orange1", InvokeStaticPrivateMethod(typeof(DashboardService), "GetTemperatureStyle", 25.0M));
        Assert.Equal("red", InvokeStaticPrivateMethod(typeof(DashboardService), "GetTemperatureStyle", 28.0M));
    }

    [Fact]
    public void UpdateDashboard_ProcessesAllSensors_StoresData()
    {
        // Arrange
        var sensors = new[]
        {
                new Sensor { Name = "S1", Location = "R1" },
                new Sensor { Name = "S2", Location = "R2" }
            };
        var data1 = new SensorData { SensorName = "S1", Temperature = 23, IsValid = true };
        var data2 = new SensorData { SensorName = "S2", Temperature = 24, IsValid = true };

        _sensorServiceMock.Setup(x => x.SimulateData(It.Is<Sensor>(s => s.Name == "S1"))).Returns(data1);
        _sensorServiceMock.Setup(x => x.SimulateData(It.Is<Sensor>(s => s.Name == "S2"))).Returns(data2);
        _sensorServiceMock.Setup(x => x.SmoothData(It.IsAny<List<SensorData>>())).Returns(23.5M);
        _sensorServiceMock.Setup(x => x.DetectAnomaly(It.IsAny<SensorData>(), It.IsAny<List<SensorData>>())).Returns(false);
        _dataServiceMock.Setup(x => x.GetRecentDataAsync(It.IsAny<int>(), 10)).ReturnsAsync(new List<SensorData>());
        _dataServiceMock.Setup(x => x.StoreDataAsync(It.IsAny<SensorData>())).Returns(Task.CompletedTask);

        var service = CreateService();
        InitializeState(service, sensors);

        // Act
        InvokePrivateMethod(service, "UpdateDashboard", CancellationToken.None);

        // Assert
        _sensorServiceMock.Verify(x => x.SimulateData(It.IsAny<Sensor>()), Times.Exactly(2));
        _dataServiceMock.Verify(x => x.StoreDataAsync(It.IsAny<SensorData>()), Times.Exactly(2));
    }

    [Fact]
    public void UpdateDashboard_SensorThrows_LogsErrorAndContinues()
    {
        // Arrange
        var sensors = new[]
        {
                new Sensor { Name = "S1", Location = "R1" },
                new Sensor { Name = "S2", Location = "R2" }
            };
        var data2 = new SensorData { SensorName = "S2", Temperature = 23, IsValid = true };

        _sensorServiceMock.Setup(x => x.SimulateData(It.Is<Sensor>(s => s.Name == "S1")))
            .Throws(new InvalidOperationException("SimFail"));
        _sensorServiceMock.Setup(x => x.SimulateData(It.Is<Sensor>(s => s.Name == "S2"))).Returns(data2);
        _sensorServiceMock.Setup(x => x.SmoothData(It.IsAny<List<SensorData>>())).Returns(23.5M);
        _sensorServiceMock.Setup(x => x.DetectAnomaly(It.IsAny<SensorData>(), It.IsAny<List<SensorData>>())).Returns(false);
        _dataServiceMock.Setup(x => x.GetRecentDataAsync(It.IsAny<int>(), 10)).ReturnsAsync(new List<SensorData>());
        _dataServiceMock.Setup(x => x.StoreDataAsync(It.IsAny<SensorData>())).Returns(Task.CompletedTask);

        var service = CreateService();
        InitializeState(service, sensors);

        // Act
        InvokePrivateMethod(service, "UpdateDashboard", CancellationToken.None);

        // Assert
        _loggerMock.VerifyLogContains("Error updating dashboard", LogLevel.Error);
    }
}

// Logger extension
internal static class LoggerMockExtensions
{
    public static void VerifyLogContains(this Mock<ILogger> logger, string message, LogLevel level = LogLevel.Information)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.AtLeastOnce);
    }

    public static void VerifyLogContains<T>(this Mock<ILogger<T>> logger, string message, LogLevel level = LogLevel.Information)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.AtLeastOnce);
    }
}