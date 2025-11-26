using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Enums;
using ThermoTracker.ThermoTracker.Models;
using ThermoTracker.ThermoTracker.Services;

namespace ThermoTracker.ThermoTracker.Tests.Services;

public class SensorServiceTests
{
    private readonly Mock<ISensorValidatorService> _mockValidator;
    private readonly Mock<ILogger<SensorService>> _mockLogger;
    private readonly Mock<IOptions<TemperatureRangeSettings>> _mockFixedRangeOptions;
    private readonly TemperatureRangeSettings _fixedRangeSettings;
    private SensorService _sensorService;

    public SensorServiceTests()
    {
        _mockValidator = new Mock<ISensorValidatorService>();
        _mockLogger = new Mock<ILogger<SensorService>>();
        _mockFixedRangeOptions = new Mock<IOptions<TemperatureRangeSettings>>();

        _fixedRangeSettings = new TemperatureRangeSettings
        {
            Min = 22.0M,
            Max = 24.0M,
            UseFixedRangeAsPrimary = true
        };

        _mockFixedRangeOptions.Setup(o => o.Value).Returns(_fixedRangeSettings);

        _sensorService = new SensorService(
            _mockValidator.Object,
            _mockLogger.Object,
            _mockFixedRangeOptions.Object);
    }

    private static Sensor CreateTestSensor(
        decimal min = 20.0M,
        decimal max = 26.0M,
        decimal normalMin = 22.0M,
        decimal normalMax = 24.0M,
        decimal noiseRange = 0.0M,
        decimal faultProbability = 0.01M,
        decimal spikeProbability = 0.005M)
    {
        return new Sensor
        {
            Id = 1,
            Name = "Test-Sensor",
            Location = "Test Location",
            MinValue = min,
            MaxValue = max,
            NormalMin = normalMin,
            NormalMax = normalMax,
            NoiseRange = noiseRange,
            FaultProbability = faultProbability,
            SpikeProbability = spikeProbability,
            IsFaulty = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static SensorConfig CreateTestSensorConfig(
        string name = "Test-Sensor",
        string location = "Test Location",
        decimal min = 20.0M,
        decimal max = 26.0M,
        decimal normalMin = 22.0M,
        decimal normalMax = 24.0M,
        decimal noise = 0.3M,
        decimal faultProb = 0.01M,
        decimal spikeProb = 0.005M)
    {
        return new SensorConfig
        {
            Name = name,
            Location = location,
            MinValue = min,
            MaxValue = max,
            NormalMin = normalMin,
            NormalMax = normalMax,
            NoiseRange = noise,
            FaultProbability = faultProb,
            SpikeProbability = spikeProb
        };
    }

    [Fact]
    public void InitializeSensors_ShouldReturnSensors_WhenValidConfigurations()
    {
        // Arrange
        var sensorConfigs = new List<SensorConfig>
            {
                CreateTestSensorConfig("S1", "L1"),
                CreateTestSensorConfig("S2", "L2")
            };

        _mockValidator.Setup(v => v.GetRules()).Returns(sensorConfigs);

        // Act
        var result = _sensor_service_initialize_for_test().InitializeSensors();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("S1", result[0].Name);
        Assert.Equal("L1", result[0].Location);
        Assert.False(result[0].IsFaulty);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Initialized sensor")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Exactly(2));
    }

    [Fact]
    public void InitializeSensors_ShouldReturnEmptyList_WhenNoConfigurations()
    {
        // Arrange
        _mockValidator.Setup(v => v.GetRules()).Returns(new List<SensorConfig>());

        // Act
        var result = _sensor_service_initialize_for_test().InitializeSensors();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetSensors_ShouldReturnEmpty_WhenNoActiveSensors()
    {
        var sensors = _sensorService.GetSensors();
        Assert.NotNull(sensors);
        Assert.Empty(sensors);
    }

    [Fact]
    public void SimulateData_ShouldGenerateValidData_UnderNormalConditions()
    {
        // Arrange
        var sensor = CreateTestSensor(noiseRange: 0.0M, faultProbability: 0.0M, spikeProbability: 0.0M);

        // Act
        var data = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(data);
        Assert.Equal(sensor.Name, data.SensorName);
        Assert.Equal(sensor.Location, data.SensorLocation);
        Assert.InRange(data.Temperature, sensor.MinValue, sensor.MaxValue);
        Assert.True(data.IsValid);
        Assert.False(data.IsFaulty);
        Assert.False(data.IsSpike);
        Assert.Equal(AlertType.None, data.AlertType);
        Assert.InRange(data.QualityScore, 0, 100);
    }

    [Fact]
    public void SimulateData_ShouldGenerateFaultyData_WhenFaultProbabilityTriggers()
    {
        // Arrange
        var sensor = CreateTestSensor(faultProbability: 1.0M, spikeProbability: 0.0M);

        // Act
        var data = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(data);
        Assert.True(data.IsFaulty);
        Assert.False(data.IsValid);
        Assert.Equal(AlertType.Fault, data.AlertType);
        Assert.True(data.Temperature == 999.99M || data.Temperature == -99.99M);
    }

    [Fact]
    public void SimulateData_ShouldGenerateSpikeData_WhenSpikeProbabilityTriggers()
    {
        // Arrange
        var sensor = CreateTestSensor(faultProbability: 0.0M, spikeProbability: 1.0M);

        // Act
        var data = _sensor_service_simulate_with_retries(sensor);

        // Assert
        Assert.NotNull(data);
        Assert.True(data.IsSpike);
        Assert.False(data.IsValid);
        Assert.Equal(AlertType.Spike, data.AlertType);
        Assert.True(data.Temperature > sensor.MaxValue + 4.9M || data.Temperature < sensor.MinValue - 4.9M);
    }

    [Fact]
    public void SimulateData_ShouldRoundTemperatureToTwoDecimals()
    {
        // Arrange
        var sensor = CreateTestSensor(noiseRange: 0.0M, faultProbability: 0.0M, spikeProbability: 0.0M);

        // Act
        var data = _sensorService.SimulateData(sensor);

        // Assert
        var rounded = Math.Round(data.Temperature, 2);
        Assert.Equal(rounded, data.Temperature);
    }

    [Fact]
    public void ValidateData_ShouldReturnTrue_WhenTemperatureInFixedRange()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData
        {
            Temperature = 23.50M,
            IsFaulty = false,
            IsSpike = false,
            SensorName = sensor.Name
        };

        // Act
        var result = _sensorService.ValidateData(sensorData, sensor);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateData_ShouldReturnFalse_WhenTemperatureBelowFixedRange()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData
        {
            Temperature = 21.90M,
            IsFaulty = false,
            IsSpike = false,
            SensorName = sensor.Name
        };

        // Act
        var result = _sensor_service_validate_with_sensor(sensorData, sensor);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateData_ShouldReturnFalse_WhenTemperatureAboveFixedRange()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData
        {
            Temperature = 24.10M,
            IsFaulty = false,
            IsSpike = false,
            SensorName = sensor.Name
        };

        // Act
        var result = _sensorService.ValidateData(sensorData, sensor);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateData_ShouldReturnFalse_WhenSensorIsFaulty()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData
        {
            Temperature = 23.50M,
            IsFaulty = true,
            IsSpike = false,
            SensorName = sensor.Name
        };

        // Act
        var result = _sensorService.ValidateData(sensorData, sensor);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateData_ShouldReturnFalse_WhenSensorHasSpike()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData
        {
            Temperature = 23.50M,
            IsFaulty = false,
            IsSpike = true,
            SensorName = sensor.Name
        };

        // Act
        var result = _sensorService.ValidateData(sensorData, sensor);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateData_ShouldUseSensorRange_WhenFixedRangeNotPrimary()
    {
        // Arrange
        _fixedRangeSettings.UseFixedRangeAsPrimary = false;
        _sensorService = new SensorService(_mockValidator.Object, _mockLogger.Object, _mockFixedRangeOptions.Object);

        var sensor = CreateTestSensor();
        sensor.MinValue = 18.0M;
        sensor.MaxValue = 28.0M;

        var sensorData = new SensorData
        {
            Temperature = 25.50M, // Outside fixed range but inside sensor's Min/Max
            IsFaulty = false,
            IsSpike = false,
            SensorName = sensor.Name
        };

        // Act
        var result = _sensorService.ValidateData(sensorData, sensor);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SmoothData_ShouldReturnAverage_WhenValidDataExists()
    {
        // Arrange
        var dataHistory = new List<SensorData>
            {
                new() { Temperature = 23.0M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.5M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 24.0M, IsValid = true, IsFaulty = false, IsSpike = false }
            };

        // Act
        var result = _sensorService.SmoothData(dataHistory);

        // Assert
        Assert.Equal(23.5M, result);
    }

    [Fact]
    public void SmoothData_ShouldExcludeInvalidData_WhenCalculatingAverage()
    {
        // Arrange
        var dataHistory = new List<SensorData>
            {
                new() { Temperature = 23.0M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 999.99M, IsValid = false, IsFaulty = true, IsSpike = false }, // Faulty
                new() { Temperature = 24.0M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 50.0M, IsValid = false, IsFaulty = false, IsSpike = true } // Spike
            };

        // Act
        var result = _sensorService.SmoothData(dataHistory);

        // Assert
        Assert.Equal(23.5M, result);
    }

    [Fact]
    public void SmoothData_ShouldReturnZero_WhenNoValidData()
    {
        // Arrange
        var dataHistory = new List<SensorData>
            {
                new() { Temperature = 999.99M, IsValid = false, IsFaulty = true, IsSpike = false },
                new() { Temperature = 50.0M, IsValid = false, IsFaulty = false, IsSpike = true }
            };

        // Act
        var result = _sensor_service_smooth_empty(dataHistory);

        // Assert
        Assert.Equal(0M, result);
    }

    [Fact]
    public void DetectAnomaly_ShouldReturnTrue_WhenDataIsFaultyOrSpike()
    {
        // Arrange
        var currentDataFaulty = new SensorData { IsFaulty = true, Temperature = 999.99M };
        var currentDataSpike = new SensorData { IsSpike = true, Temperature = 50.0M };

        // Act & Assert
        Assert.True(_sensorService.DetectAnomaly(currentDataFaulty, new List<SensorData>()));
        Assert.True(_sensorService.DetectAnomaly(currentDataSpike, new List<SensorData>()));
    }

    [Fact]
    public void DetectAnomaly_ShouldReturnFalse_WhenInsufficientRecentData()
    {
        // Arrange
        var currentData = new SensorData { Temperature = 25.0M, IsFaulty = false, IsSpike = false };
        var recentData = new List<SensorData>
            {
                new() { Temperature = 23.0M, IsValid = true },
                new() { Temperature = 23.5M, IsValid = true }
            };

        // Act
        var result = _sensorService.DetectAnomaly(currentData, recentData);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DetectAnomaly_ShouldReturnTrue_WhenTemperatureIsStatisticalOutlier()
    {
        // Arrange
        var currentData = new SensorData { Temperature = 30.0M, IsFaulty = false, IsSpike = false };
        var recentData = new List<SensorData>
            {
                new() { Temperature = 23.0M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.1M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.2M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.3M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.4M, IsValid = true, IsFaulty = false, IsSpike = false }
            };

        // Act
        var result = _sensorService.DetectAnomaly(currentData, recentData);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DetectAnomaly_ShouldReturnFalse_WhenTemperatureIsWithinNormalRange()
    {
        // Arrange
        var currentData = new SensorData { Temperature = 23.3M, IsFaulty = false, IsSpike = false };
        var recentData = new List<SensorData>
            {
                new() { Temperature = 23.0M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.1M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.2M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.3M, IsValid = true, IsFaulty = false, IsSpike = false },
                new() { Temperature = 23.4M, IsValid = true, IsFaulty = false, IsSpike = false }
            };

        // Act
        var result = _sensorService.DetectAnomaly(currentData, recentData);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CheckThreshold_ShouldReturnTrue_WhenTemperatureOutsideFixedRange()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorDataLow = new SensorData { Temperature = 21.9M };
        var sensorDataHigh = new SensorData { Temperature = 24.1M };

        // Act & Assert
        Assert.True(_sensorService.CheckThreshold(sensorDataLow, sensor));
        Assert.True(_sensorService.CheckThreshold(sensorDataHigh, sensor));
    }

    [Fact]
    public void CheckThreshold_ShouldUseCustomThresholds_WhenProvided_AndFixedRangeNotPrimary()
    {
        // Arrange
        _fixedRangeSettings.UseFixedRangeAsPrimary = false;
        _sensorService = new SensorService(_mockValidator.Object, _mockLogger.Object, _mockFixedRangeOptions.Object);

        var sensor = CreateTestSensor();
        var sensorData = new SensorData { Temperature = 25.0M };
        decimal customMin = 24.0M;
        decimal customMax = 26.0M;

        // Act
        var result = _sensorService.CheckThreshold(sensorData, sensor, customMin, customMax);

        // Assert -> 25.0 is within 24-26 -> should be false (not outside)
        Assert.False(result);
    }

    [Fact]
    public void InjectClearShutdownStartFaultMethods_ShouldUpdateSensorState_AndLog()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.IsFaulty = false;

        // Act - Inject
        _sensorService.InjectFault(sensor);
        Assert.True(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Manually injected fault")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);

        // Act - Clear
        _sensorService.ClearFault(sensor);
        Assert.False(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cleared fault")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);

        // Act - Shutdown
        _sensorService.ShutdownSensor(sensor);
        Assert.True(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("has been shut down")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);

        // Act - Start
        _sensorService.StartSensor(sensor);
        Assert.False(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("has been started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public void QualityScore_ShouldBeWithinValidRange_Always()
    {
        // Arrange
        var sensor = CreateTestSensor(noiseRange: 0.0M, faultProbability: 0.0M, spikeProbability: 0.0M);

        // Act
        var data = _sensor_service_simulate_with_retries(sensor);

        // Assert
        Assert.InRange(data.QualityScore, 0, 100);
    }

    [Fact]
    public void SimulateData_ShouldSetQualityScore_ForValidAndFaultyData()
    {
        // Arrange - valid data
        var sensorValid = CreateTestSensor(noiseRange: 0.0M, faultProbability: 0.0M, spikeProbability: 0.0M);
        var validData = _sensorService.SimulateData(sensorValid);

        Assert.InRange(validData.QualityScore, 0, 100);
        if (!validData.IsValid)
        {
            Assert.Equal(0, validData.QualityScore);
        }
        else
        {
            Assert.True(validData.QualityScore >= 0 && validData.QualityScore <= 100);
        }

        // Arrange - faulty data forced
        var sensorFault = CreateTestSensor(faultProbability: 1.0M);
        var faultyData = _sensorService.SimulateData(sensorFault);

        Assert.True(faultyData.IsFaulty);
        Assert.InRange(faultyData.QualityScore, 0, 100);
    }

    private SensorData _sensor_service_simulate_with_retries(Sensor sensor, int attempts = 10)
    {
        for (var i = 0; i < attempts; i++)
        {
            var d = _sensorService.SimulateData(sensor);
            if (sensor.SpikeProbability >= 1.0M && d.IsSpike) return d;
            if (sensor.FaultProbability >= 1.0M && d.IsFaulty) return d;
            if (sensor.SpikeProbability == 0.0M && sensor.FaultProbability == 0.0M && !d.IsSpike && !d.IsFaulty) return d;
            // else try again
        }

        return _sensorService.SimulateData(sensor);
    }

    private SensorService _sensor_service_initialize_for_test()
    {
        return new SensorService(_mockValidator.Object, _mockLogger.Object, _mockFixedRangeOptions.Object);
    }

    private bool _sensor_service_validate_with_sensor(SensorData data, Sensor sensor)
    {
        return _sensorService.ValidateData(data, sensor);
    }

    private decimal _sensor_service_smooth_empty(List<SensorData> list)
    {
        return _sensorService.SmoothData(list);
    }
}