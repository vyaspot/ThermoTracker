using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;
public interface ISensorValidatorService
{
    List<SensorConfig> GetRules();
}