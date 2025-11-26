namespace ThermoTracker.ThermoTracker.Models;

public class OverallStatistics
{
    public int TotalSensors { get; set; }
    public int TotalReadings { get; set; }
    public int TotalAnomalies { get; set; }
    public int TotalSpikes { get; set; }
    public int TotalFaults { get; set; }
    public decimal OverallAverageTemperature { get; set; }
}