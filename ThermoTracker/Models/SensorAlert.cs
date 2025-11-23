using ThermoTracker.ThermoTracker.Enums;

namespace ThermoTracker.ThermoTracker.Models;

public class SensorAlert
{
    public string SensorName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal Temperature { get; set; }
    public AlertType AlertType { get; set; } = AlertType.None;
    public string Message { get; set; } = string.Empty;
}