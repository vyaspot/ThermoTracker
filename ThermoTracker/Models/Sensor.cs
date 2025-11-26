namespace ThermoTracker.ThermoTracker.Models;

public class Sensor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public decimal MinValue { get; set; }
    public decimal MaxValue { get; set; }
    public decimal NormalMin { get; set; }
    public decimal NormalMax { get; set; }
    public decimal NoiseRange { get; set; }
    public decimal FaultProbability { get; set; }
    public decimal SpikeProbability { get; set; }
    public bool IsFaulty { get; set; }
    public bool IsOnline { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReadingTime { get; set; }
    public int TotalReadings { get; set; }
    public int ErrorCount { get; set; }
    public decimal LastTemperature { get; set; }

    public virtual ICollection<SensorData> SensorDataRecords { get; set; } = new List<SensorData>();
}