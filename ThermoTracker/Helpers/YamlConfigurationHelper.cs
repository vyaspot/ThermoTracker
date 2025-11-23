using ThermoTracker.ThermoTracker.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ThermoTracker.ThermoTracker.Helpers;

public static class YamlConfigurationHelper
{
    public static List<SensorConfig> LoadSensorConfigs(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"YAML configuration file not found: {filePath}");
        }

        try
        {
            var yamlContent = File.ReadAllText(filePath);

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