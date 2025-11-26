using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public interface ISensorConfigWatcher
{
    event Action<List<SensorConfig>> OnConfigChanged;
}