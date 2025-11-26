using ThermoTracker.ThermoTracker.Helpers;

namespace ThermoTracker.ThermoTracker.Tests.Helpers;

public class YamlConfigurationHelperTests
{
    [Fact]
    public void LoadSensorConfigs_ValidYaml_ReturnsSensorConfigs()
    {
        // Arrange
        var yaml = @"
            sensors:
              - name: TemperatureSensor
                location: Lab1
                minValue: 10
                maxValue: 50
                normalMin: 22
                normalMax: 24
                noiseRange: 0.5
                faultProbability: 0.01
                spikeProbability: 0.005
              - name: HumiditySensor
                location: Lab2
                minValue: 30
                maxValue: 70
                normalMin: 40
                normalMax: 60
                noiseRange: 1.0
                faultProbability: 0.02
                spikeProbability: 0.01
            ";

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, yaml);

        // Act
        var configs = YamlConfigurationHelper.LoadSensorConfigs(tempFile);

        // Assert
        Assert.NotNull(configs);
        Assert.Equal(2, configs.Count);

        Assert.Equal("TemperatureSensor", configs[0].Name);
        Assert.Equal("Lab1", configs[0].Location);
        Assert.Equal(10, configs[0].MinValue);
        Assert.Equal(50, configs[0].MaxValue);

        Assert.Equal("HumiditySensor", configs[1].Name);
        Assert.Equal("Lab2", configs[1].Location);

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public void LoadSensorConfigs_FileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".yaml");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => YamlConfigurationHelper.LoadSensorConfigs(fakePath));
    }

    [Fact]
    public void LoadSensorConfigs_InvalidYamlStructure_ThrowsInvalidDataException()
    {
        // Arrange
        var yaml = @"
invalidKey:
  - name: SensorX
    location: LabX
";
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, yaml);

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => YamlConfigurationHelper.LoadSensorConfigs(tempFile));

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public void LoadSensorConfigs_MalformedYaml_ThrowsInvalidDataException()
    {
        // Arrange
        var yaml = @"sensors: - name: SensorX location"; // broken YAML
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, yaml);

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => YamlConfigurationHelper.LoadSensorConfigs(tempFile));

        // Cleanup
        File.Delete(tempFile);
    }
}