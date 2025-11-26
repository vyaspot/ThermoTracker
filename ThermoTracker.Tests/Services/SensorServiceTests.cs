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
    private readonly SensorService _sensorService;



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

    [Fact]
    public void InitializeSensors_ShouldReturnSensors_WhenValidConfigurations()
    {
        // Arrange
        var sensorConfigs = new List<SensorConfig>
    {
        new() {
            Name = "Test-Sensor-1",
            Location = "Test Location A",
            MinValue = 20.0M,
            MaxValue = 26.0M,
            NormalMin = 22.0M,
            NormalMax = 24.0M,
            NoiseRange = 0.3M,
            FaultProbability = 0.01M,
            SpikeProbability = 0.005M
        },
        new() {
            Name = "Test-Sensor-2",
            Location = "Test Location B",
            MinValue = 19.0M,
            MaxValue = 25.0M,
            NormalMin = 22.0M,
            NormalMax = 24.0M,
            NoiseRange = 0.4M,
            FaultProbability = 0.015M,
            SpikeProbability = 0.008M
        }
    };

        _mockValidator.Setup(v => v.GetRules()).Returns(sensorConfigs);

        // Act - Call InitializeSensors instead of GetSensors
        var result = _sensorService.InitializeSensors();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Test-Sensor-1", result[0].Name);
        Assert.Equal("Test Location A", result[0].Location);
        Assert.Equal(20.0M, result[0].MinValue);
        Assert.Equal(26.0M, result[0].MaxValue);
        Assert.False(result[0].IsFaulty);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Initialized sensor")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)!),
            Times.Exactly(2));
    }

    [Fact]
    public void InitializeSensors_ShouldReturnEmptyList_WhenNoConfigurations()
    {
        // Arrange
        _mockValidator.Setup(v => v.GetRules()).Returns(new List<SensorConfig>());

        // Act
        var result = _sensorService.GetSensors();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void SimulateData_ShouldGenerateValidData_UnderNormalConditions()
    {
        // Arrange
        var sensor = CreateTestSensor();

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sensor.Name, result.SensorName);
        Assert.Equal(sensor.Location, result.SensorLocation);
        Assert.InRange(result.Temperature, sensor.MinValue, sensor.MaxValue);
        Assert.True(result.IsValid);
        Assert.False(result.IsFaulty);
        Assert.False(result.IsSpike);
        Assert.Equal(AlertType.None, result.AlertType);
        Assert.InRange(result.QualityScore, 0, 100);
    }

    [Fact]
    public void SimulateData_ShouldGenerateFaultyData_WhenFaultProbabilityTriggers()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.FaultProbability = 1.0M; // 100% chance of fault

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsFaulty);
        Assert.False(result.IsValid);
        Assert.Equal(AlertType.Fault, result.AlertType);
        Assert.True(result.Temperature == 999.99M || result.Temperature == -99.99M);
    }

    [Fact]
    public void SimulateData_ShouldGenerateSpikeData_WhenSpikeProbabilityTriggers()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.SpikeProbability = 1.0M; // 100% chance of spike

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSpike);
        Assert.False(result.IsValid);
        Assert.Equal(AlertType.Spike, result.AlertType);
        Assert.True(result.Temperature > sensor.MaxValue + 4.9M || result.Temperature < sensor.MinValue - 4.9M);
    }

    [Fact]
    public void SimulateData_ShouldRoundTemperatureToTwoDecimals()
    {
        // Arrange
        var sensor = CreateTestSensor();

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(result);
        var rounded = Math.Round(result.Temperature, 2);
        Assert.Equal(rounded, result.Temperature);
    }

    [Fact]
    public void ValidateData_ShouldReturnTrue_WhenTemperatureInFixedRange()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData
        {
            Temperature = 23.5M,
            IsFaulty = false,
            IsSpike = false
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
            Temperature = 21.9M,
            IsFaulty = false,
            IsSpike = false
        };

        // Act
        var result = _sensorService.ValidateData(sensorData, sensor);

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
            Temperature = 24.1M,
            IsFaulty = false,
            IsSpike = false
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
            Temperature = 23.5M,
            IsFaulty = true,
            IsSpike = false
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
            Temperature = 23.5M,
            IsFaulty = false,
            IsSpike = true
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
        var sensor = CreateTestSensor();
        sensor.MinValue = 18.0M;
        sensor.MaxValue = 28.0M;

        var sensorData = new SensorData
        {
            Temperature = 25.5M, // Outside fixed range but within sensor range
            IsFaulty = false,
            IsSpike = false
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
        Assert.Equal(23.5M, result); // Only 23.0 and 24.0 should be used
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
        var result = _sensorService.SmoothData(dataHistory);

        // Assert
        Assert.Equal(0M, result);
    }

    [Fact]
    public void SmoothData_ShouldReturnZero_WhenEmptyHistory()
    {
        // Arrange
        var dataHistory = new List<SensorData>();

        // Act
        var result = _sensorService.SmoothData(dataHistory);

        // Assert
        Assert.Equal(0M, result);
    }

    [Fact]
    public void DetectAnomaly_ShouldReturnTrue_WhenDataIsFaulty()
    {
        // Arrange
        var currentData = new SensorData { IsFaulty = true, Temperature = 999.99M };
        var recentData = new List<SensorData>();

        // Act
        var result = _sensorService.DetectAnomaly(currentData, recentData);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DetectAnomaly_ShouldReturnTrue_WhenDataIsSpike()
    {
        // Arrange
        var currentData = new SensorData { IsSpike = true, Temperature = 50.0M };
        var recentData = new List<SensorData>();

        // Act
        var result = _sensorService.DetectAnomaly(currentData, recentData);

        // Assert
        Assert.True(result);
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
    public void CheckThreshold_ShouldReturnTrue_WhenTemperatureBelowThreshold()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData { Temperature = 21.9M };

        // Act
        var result = _sensorService.CheckThreshold(sensorData, sensor);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CheckThreshold_ShouldReturnTrue_WhenTemperatureAboveThreshold()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData { Temperature = 24.1M };

        // Act
        var result = _sensorService.CheckThreshold(sensorData, sensor);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CheckThreshold_ShouldReturnFalse_WhenTemperatureWithinThreshold()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData { Temperature = 23.5M };

        // Act
        var result = _sensorService.CheckThreshold(sensorData, sensor);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CheckThreshold_ShouldUseCustomThresholds_WhenProvided()
    {
        // Arrange
        var sensor = CreateTestSensor();
        var sensorData = new SensorData { Temperature = 25.0M };
        decimal customMin = 24.0M;
        decimal customMax = 26.0M;

        // Act
        _fixedRangeSettings.UseFixedRangeAsPrimary = false;
        var result = _sensorService.CheckThreshold(sensorData, sensor, customMin, customMax);

        // Assert
        Assert.False(result); // 25.0 is within 24.0-26.0
    }

    [Fact]
    public void CheckThreshold_ShouldUseSensorRange_WhenFixedRangeNotPrimary()
    {
        // Arrange
        _fixedRangeSettings.UseFixedRangeAsPrimary = false;
        var sensor = CreateTestSensor();
        sensor.NormalMin = 20.0M;
        sensor.NormalMax = 26.0M;
        var sensorData = new SensorData { Temperature = 23.5M };

        // Act
        var result = _sensorService.CheckThreshold(sensorData, sensor);

        // Assert
        Assert.False(result); // 23.5 is within 20.0-26.0
    }

    [Fact]
    public void InjectFault_ShouldSetSensorToFaulty()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.IsFaulty = false;

        // Act
        _sensorService.InjectFault(sensor);

        // Assert
        Assert.True(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Manually injected fault")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)!),
            Times.Once);
    }

    [Fact]
    public void ClearFault_ShouldSetSensorToNotFaulty()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.IsFaulty = true;

        // Act
        _sensorService.ClearFault(sensor);

        // Assert
        Assert.False(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cleared fault")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)!),
            Times.Once);
    }

    [Fact]
    public void ShutdownSensor_ShouldSetSensorToFaulty()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.IsFaulty = false;

        // Act
        _sensorService.ShutdownSensor(sensor);

        // Assert
        Assert.True(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("has been shut down")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)!),
            Times.Once);
    }

    [Fact]
    public void StartSensor_ShouldSetSensorToNotFaulty()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.IsFaulty = true;

        // Act
        _sensorService.StartSensor(sensor);

        // Assert
        Assert.False(sensor.IsFaulty);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("has been started")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)!),
            Times.Once);
    }

    [Fact]
    public void QualityScore_ShouldBeWithinValidRange_Always()
    {
        // Arrange
        var sensor = CreateTestSensor();

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.InRange(result.QualityScore, 0, 100);
    }

    [Fact]
    public void SimulateData_ShouldSetQualityScore_ForValidData()
    {
        // Arrange
        var sensor = CreateTestSensor();

        sensor.NormalMin = 22.5M;
        sensor.NormalMax = 23.5M;
        sensor.NoiseRange = 0.0M;

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.NotNull(result);
        Assert.InRange(result.QualityScore, 0, 100);

        if (!result.IsValid)
        {
            Assert.Equal(0, result.QualityScore);
        }
        else
        {
            Assert.True(result.QualityScore > 0,
                $"Quality score should be >0 for valid data, but was {result.QualityScore}");
        }
    }

    [Fact]
    public void SimulateData_ShouldSetQualityScore_ForFaultyData()
    {
        // Arrange
        var sensor = CreateTestSensor();
        sensor.FaultProbability = 1.0M; // Force fault

        // Act
        var result = _sensorService.SimulateData(sensor);

        // Assert
        Assert.True(result.IsFaulty);
        Assert.InRange(result.QualityScore, 0, 100);
    }

    private static Sensor CreateTestSensor()
    {
        return new Sensor
        {
            Name = "Test-Sensor",
            Location = "Test Location",
            MinValue = 20.0M,
            MaxValue = 26.0M,
            NormalMin = 22.0M,
            NormalMax = 24.0M,
            NoiseRange = 0.3M,
            FaultProbability = 0.01M,
            SpikeProbability = 0.005M,
            IsFaulty = false,
            CreatedAt = DateTime.UtcNow
        };
    }
}