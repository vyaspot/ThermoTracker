using ThermoTracker.ThermoTracker.Helpers;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public class SensorValidatorService : ISensorValidatorService
{
    public List<SensorConfig> GetRules()
    {
        var configs = YamlConfigurationHelper.LoadSensorConfigs("sensors.yml");

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

        if (config.FaultProbability < 0 || config.FaultProbability > 1)
            throw new ArgumentException("Fault probability must be between 0 and 1");

        if (config.SpikeProbability < 0 || config.SpikeProbability > 1)
            throw new ArgumentException("Spike probability must be between 0 and 1");
    }
}