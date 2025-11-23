namespace ThermoTracker.ThermoTracker.Configurations;

public class FixedTemperatureRange
{
    public decimal Min { get; set; } = 22.0M;
    public decimal Max { get; set; } = 24.0M;
    public bool UseFixedRangeAsPrimary { get; set; } = true;
}