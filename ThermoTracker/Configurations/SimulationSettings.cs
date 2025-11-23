namespace ThermoTracker.ThermoTracker.Configurations;

public class SimulationSettings
{
    public int UpdateIntervalMs { get; set; }
    public int DataHistorySize { get; set; }
    public int MovingAverageWindow { get; set; }
    public decimal AnomalyThreshold { get; set; }
}