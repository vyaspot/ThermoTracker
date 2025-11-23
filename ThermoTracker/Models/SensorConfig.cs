namespace ThermoTracker.ThermoTracker.Models;

public class SensorConfig
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    public decimal MinValue { get; set; }

    public decimal MaxValue { get; set; }

    public decimal NormalMin { get; set; } = 22.0M;

    public decimal NormalMax { get; set; } = 24.0M;

    public decimal NoiseRange { get; set; } = 0.5M;

    public decimal FaultProbability { get; set; } = 0.01M;

    public decimal SpikeProbability { get; set; } = 0.005M;
}