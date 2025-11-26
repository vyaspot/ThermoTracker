using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ThermoTracker.ThermoTracker.Enums;

namespace ThermoTracker.ThermoTracker.Models;

public class SensorData
{
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string SensorName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string SensorLocation { get; set; } = string.Empty;

    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(5,2)")]
    [Range(-99.99, 999.99)]
    public decimal Temperature { get; set; }

    public bool IsValid { get; set; }
    public bool IsAnomaly { get; set; }
    public bool IsSpike { get; set; }
    public bool IsFaulty { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    [Range(-99.99, 999.99)]
    public decimal SmoothedValue { get; set; }

    public AlertType AlertType { get; set; } = AlertType.None;

    // Additional metadata
    public string? Notes { get; set; }
    public int QualityScore { get; set; } = 100;


    // Foreign key relationship
    public int SensorId { get; set; }
    public virtual Sensor Sensor { get; set; } = default!;
}