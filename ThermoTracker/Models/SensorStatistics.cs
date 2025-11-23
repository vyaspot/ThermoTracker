namespace ThermoTracker.ThermoTracker.Models;

public class SensorStatistics
{
    public string SensorName { get; set; } = string.Empty;
    public int TotalReadings { get; set; }
    public int ValidReadings { get; set; }
    public int AnomalyReadings { get; set; }
    public int SpikeReadings { get; set; }
    public int FaultReadings { get; set; }
    public decimal AverageTemperature { get; set; }
    public decimal MinTemperature { get; set; }
    public decimal MaxTemperature { get; set; }
}