using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public interface ISensorService
{
    List<Sensor> InitializeSensors();
    List<Sensor> GetSensors();
    SensorData SimulateData(Sensor sensor);
    bool ValidateData(SensorData data, Sensor sensor);
    decimal SmoothData(List<SensorData> dataHistory);
    bool DetectAnomaly(SensorData currentData, List<SensorData> recentData);
    void InjectFault(Sensor sensor);
    void ClearFault(Sensor sensor);
    void ShutdownSensor(Sensor sensor);
    bool CheckThreshold(SensorData data, Sensor sensor, decimal? customMin = null, decimal? customMax = null);
}