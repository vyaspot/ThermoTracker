using Moq;
using ThermoTracker.ThermoTracker.Models;
using ThermoTracker.ThermoTracker.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ThermoTracker.ThermoTracker.Tests.Services;

public class SensorValidatorServiceTests
{
    private readonly SensorValidatorService _sensorValidatorService;
    private readonly Mock<IFileService> _fileServiceMock;

    public SensorValidatorServiceTests()
    {
        _fileServiceMock = new Mock<IFileService>();
        _sensorValidatorService = new SensorValidatorService(_fileServiceMock.Object);
    }

    [Fact]
    public void GetRules_WithValidConfigs_ReturnsAllConfigs()
    {
        // Arrange
        var mockConfigs = new List<SensorConfig>
            {
                new SensorConfig
                {
                    Name = "TempSensor1",
                    Location = "Room1",
                    MinValue = 0,
                    MaxValue = 100,
                    NormalMin = 20,
                    NormalMax = 80,
                    NoiseRange = 0.5M,
                    FaultProbability = 0.05M,
                    SpikeProbability = 0.02M
                },
                new SensorConfig
                {
                    Name = "HumiditySensor1",
                    Location = "Room2",
                    MinValue = 0,
                    MaxValue = 100,
                    NormalMin = 30,
                    NormalMax = 70,
                    NoiseRange = 0.3M,
                    FaultProbability = 0.03M,
                    SpikeProbability = 0.01M
                }
            };

        var yamlContent = @"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02
            - name: HumiditySensor1
              location: Room2
              minValue: 0
              maxValue: 100
              normalMin: 30
              normalMax: 70
              noiseRange: 0.3
              faultProbability: 0.03
              spikeProbability: 0.01";

        SetupFileService("sensors.yml", yamlContent);

        // Act
        var result = _sensorValidatorService.GetRules();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("TempSensor1", result[0].Name);
        Assert.Equal("HumiditySensor1", result[1].Name);
    }

    [Fact]
    public void GetRules_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: """"
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Sensor name cannot be empty", exception.Message);
    }

    [Fact]
    public void GetRules_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Sensor name cannot be empty", exception.Message);
    }

    [Fact]
    public void GetRules_WithWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: ""   ""
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Sensor name cannot be empty", exception.Message);
    }

    [Fact]
    public void GetRules_WithEmptyLocation_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: TempSensor1
              location: """"
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Sensor location cannot be empty", exception.Message);
    }

    [Fact]
    public void GetRules_WithMinValueEqualToMaxValue_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 100
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("MinValue must be less than MaxValue", exception.Message);
    }

    [Fact]
    public void GetRules_WithMinValueGreaterThanMaxValue_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 150
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("MinValue must be less than MaxValue", exception.Message);
    }

    [Fact]
    public void GetRules_WithNormalMinLessThanMinValue_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: -10
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Normal range must be within min/max range", exception.Message);
    }

    [Fact]
    public void GetRules_WithNormalMaxGreaterThanMaxValue_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 150
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Normal range must be within min/max range", exception.Message);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("2.0")]
    public void GetRules_WithInvalidFaultProbability_ThrowsArgumentException(string faultProbability)
    {
        // Arrange
        var yamlContent = $@"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: {faultProbability}
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Fault probability must be between 0 and 1", exception.Message);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("2.0")]
    public void GetRules_WithInvalidSpikeProbability_ThrowsArgumentException(string spikeProbability)
    {
        // Arrange
        var yamlContent = $@"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: 0.5
              faultProbability: 0.05
              spikeProbability: {spikeProbability}";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Spike probability must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void GetRules_WithNoiseRangeNegativeValue_ThrowsArgumentException()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: TempSensor1
              location: Room1
              minValue: 0
              maxValue: 100
              normalMin: 20
              normalMax: 80
              noiseRange: -0.5
              faultProbability: 0.05
              spikeProbability: 0.02";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _sensorValidatorService.GetRules());
        Assert.Equal("Noise range must be non-negative", exception.Message);
    }

    [Fact]
    public void GetRules_WithValidBoundaryValues_ReturnsConfigs()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: BoundarySensor
              location: BoundaryRoom
              minValue: 0
              maxValue: 1
              normalMin: 0
              normalMax: 1
              noiseRange: 0
              faultProbability: 0.0
              spikeProbability: 1.0";

        SetupFileService("sensors.yml", yamlContent);

        // Act
        var result = _sensorValidatorService.GetRules();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("BoundarySensor", result[0].Name);
    }

    [Fact]
    public void GetRules_WithFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        _fileServiceMock.Setup(f => f.Exists("sensors.yml")).Returns(false);

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() => _sensorValidatorService.GetRules());
        Assert.Contains("YAML configuration file not found", exception.Message);
    }

    [Fact]
    public void GetRules_WithInvalidYamlStructure_ThrowsInvalidDataException()
    {
        // Arrange
        var yamlContent = @"invalid_structure:
            - name: TempSensor1";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(() => _sensorValidatorService.GetRules());
        Assert.Contains("'sensors' key not found", exception.Message);
    }

    [Fact]
    public void GetRules_WithInvalidYamlFormat_ThrowsInvalidDataException()
    {
        // Arrange
        var yamlContent = @"invalid yaml content: [";

        SetupFileService("sensors.yml", yamlContent);

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(() => _sensorValidatorService.GetRules());
        Assert.Contains("Failed to load configuration", exception.Message);
    }

    [Fact]
    public void GetRules_WithDefaultValues_UsesDefaultValuesCorrectly()
    {
        // Arrange
        var yamlContent = @"sensors:
            - name: DefaultSensor
              location: DefaultRoom
              minValue: 0
              maxValue: 100";

        SetupFileService("sensors.yml", yamlContent);

        // Act
        var result = _sensorValidatorService.GetRules();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        var config = result[0];
        Assert.Equal(22.0M, config.NormalMin);
        Assert.Equal(24.0M, config.NormalMax);
        Assert.Equal(0.5M, config.NoiseRange);
        Assert.Equal(0.01M, config.FaultProbability);
        Assert.Equal(0.005M, config.SpikeProbability);
    }

    private void SetupFileService(string filePath, string yamlContent)
    {
        _fileServiceMock.Setup(f => f.Exists(filePath)).Returns(true);
        _fileServiceMock.Setup(f => f.ReadAllText(filePath)).Returns(yamlContent);
    }
}

// Interface for file operations to make testing easier
public interface IFileService
{
    bool Exists(string path);
    string ReadAllText(string path);
}

// Updated SensorValidatorService with dependency injection
public class SensorValidatorService : ISensorValidatorService
{
    private readonly IFileService _fileService;

    public SensorValidatorService(IFileService fileService)
    {
        _fileService = fileService;
    }

    public SensorValidatorService() : this(new DefaultFileService())
    {
    }

    public List<SensorConfig> GetRules()
    {
        var configs = YamlConfigurationHelper.LoadSensorConfigs("sensors.yml", _fileService);

        foreach (var config in configs)
        {
            ValidateSensorConfig(config);
        }

        return configs;
    }

    private static void ValidateSensorConfig(SensorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            throw new ArgumentException("Sensor name cannot be empty");

        if (string.IsNullOrWhiteSpace(config.Location))
            throw new ArgumentException("Sensor location cannot be empty");

        if (config.MinValue >= config.MaxValue)
            throw new ArgumentException("MinValue must be less than MaxValue");

        if (config.NormalMin < config.MinValue || config.NormalMax > config.MaxValue)
            throw new ArgumentException("Normal range must be within min/max range");

        if (config.NoiseRange < 0)
            throw new ArgumentException("Noise range must be non-negative");

        if (config.FaultProbability < 0 || config.FaultProbability > 1)
            throw new ArgumentException("Fault probability must be between 0 and 1");

        if (config.SpikeProbability < 0 || config.SpikeProbability > 1)
            throw new ArgumentException("Spike probability must be between 0 and 1");
    }
}

// Default file service implementation
public class DefaultFileService : IFileService
{
    public bool Exists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
}

// Extended YamlConfigurationHelper with file service dependency
public static class YamlConfigurationHelper
{
    public static List<SensorConfig> LoadSensorConfigs(string filePath, IFileService fileService = null!)
    {
        fileService ??= new DefaultFileService();

        if (!fileService.Exists(filePath))
        {
            throw new FileNotFoundException($"YAML configuration file not found: {filePath}");
        }

        try
        {
            var yamlContent = fileService.ReadAllText(filePath);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlObject = deserializer.Deserialize<Dictionary<string, List<SensorConfig>>>(yamlContent);

            if (yamlObject != null && yamlObject.ContainsKey("sensors"))
            {
                return yamlObject["sensors"];
            }

            throw new InvalidDataException("Invalid YAML structure: 'sensors' key not found");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to load configuration from file '{filePath}': {ex.Message}", ex);
        }
    }
}