using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Enums;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public class SensorService(
    ISensorValidatorService validator,
    ILogger<SensorService> logger,
    IOptions<TemperatureRangeSettings> fixedRangeOptions) : ISensorService
{
    private readonly Random _random = new();
    private readonly ISensorValidatorService _validatorService = validator;
    private readonly ILogger<SensorService> _logger = logger;
    private readonly TemperatureRangeSettings _fixedRange = fixedRangeOptions.Value;
    

    public List<Sensor> InitializeSensors()
    {
        var validSensors = _validatorService.GetRules();
        var sensors = new List<Sensor>();

        foreach (var validSensor in validSensors)
        {
            var sensor = new Sensor
            {
                Name = validSensor.Name,
                Location = validSensor.Location,
                MinValue = validSensor.MinValue,
                MaxValue = validSensor.MaxValue,
                NormalMin = validSensor.NormalMin,
                NormalMax = validSensor.NormalMax,
                NoiseRange = validSensor.NoiseRange,
                FaultProbability = validSensor.FaultProbability,
                SpikeProbability = validSensor.SpikeProbability,
                IsFaulty = false,
                CreatedAt = DateTime.UtcNow
            };
            sensors.Add(sensor);

            _logger.LogInformation(
                "Initialized sensor: {Name} at {Location} (Range: {Min}-{Max}°C, Fixed Validation: {FixedMin}-{FixedMax}°C)",
                sensor.Name, sensor.Location, sensor.MinValue, sensor.MaxValue,
                _fixedRange.Min, _fixedRange.Max);
        }

        return sensors;
    }

    public SensorData SimulateData(Sensor sensor)
    {
        decimal temperature;
        bool isSpike = false;
        bool isFaulty = sensor.IsFaulty;
        var alertType = AlertType.None;

        // Check for complete sensor failure
        if (!isFaulty && _random.NextDouble() < (double)sensor.FaultProbability)
        {
            _logger.LogWarning("Injecting fault into sensor: {SensorName}", sensor.Name);
            isFaulty = true;
            alertType = AlertType.Fault;
        }

        if (isFaulty)
        {
            // Simulate complete sensor failure - return extreme but valid decimal(5,2) value
            temperature = _random.NextDouble() > 0.5 ? 999.99M : -99.99M;
        }
        else if (_random.NextDouble() < (double)sensor.SpikeProbability)
        {
            // Simulate temperature spike within decimal(5,2) bounds WITH PROPER ROUNDING
            var spikeMagnitude = (decimal)(_random.NextDouble() * 10.0);

            if (_random.NextDouble() > 0.5)
            {
                // Hot spike - ensure it doesn't exceed 999.99 and is properly rounded
                temperature = Math.Min(sensor.MaxValue + 5.0M + spikeMagnitude, 999.99M);
            }
            else
            {
                // Cold spike - ensure it doesn't go below -99.99 and is properly rounded
                temperature = Math.Max(sensor.MinValue - 5.0M - spikeMagnitude, -99.99M);
            }

            // CRITICAL FIX: Round the spike temperature to 2 decimal places
            temperature = Math.Round(temperature, 2);

            isSpike = true;
            alertType = AlertType.Spike;
            _logger.LogWarning("Temperature spike detected on sensor: {SensorName} - {Temperature}°C",
                sensor.Name, temperature);
        }
        else
        {
            // Normal reading with noise around normal range
            var baseTemp = sensor.NormalMin + (decimal)_random.NextDouble() * (sensor.NormalMax - sensor.NormalMin);
            var noise = ((decimal)_random.NextDouble() - 0.5M) * 2.0M * sensor.NoiseRange;
            temperature = Math.Round(baseTemp + noise, 2);

            // Ensure temperature stays within decimal(5,2) bounds
            temperature = Math.Max(-99.99M, Math.Min(999.99M, temperature));
        }

        var data = new SensorData
        {
            SensorId = sensor.Id,
            SensorName = sensor.Name,
            SensorLocation = sensor.Location,
            Temperature = temperature,
            IsSpike = isSpike,
            IsFaulty = isFaulty,
            Timestamp = DateTime.UtcNow,
            AlertType = alertType
        };

        // Validate using dual validation system
        data.IsValid = ValidateData(data, sensor);
        data.IsAnomaly = data.IsSpike || data.IsFaulty || CheckThreshold(data, sensor);

        // Determine alert type
        data.AlertType = DetermineAlertType(data, sensor);

        // Calculate quality score
        data.QualityScore = CalculateQualityScore(data, sensor);

        return data;
    }

    private AlertType DetermineAlertType(SensorData data, Sensor sensor)
    {
        if (data.IsFaulty) return AlertType.Fault;
        if (data.IsSpike) return AlertType.Spike;
        if (CheckThreshold(data, sensor)) return AlertType.Threshold;
        if (data.IsAnomaly) return AlertType.Anomaly;
        return AlertType.None;
    }

    private int CalculateQualityScore(SensorData data, Sensor sensor)
    {
        if (!data.IsValid) return 0;
        if (data.IsFaulty || data.IsSpike) return 10;

        // Calculate score based on how close to fixed normal range
        var midPoint = (_fixedRange.Min + _fixedRange.Max) / 2;
        var deviation = Math.Abs(data.Temperature - midPoint);
        var maxDeviation = (_fixedRange.Max - _fixedRange.Min) / 2;

        var score = 100 - (int)((deviation / maxDeviation) * 50);
        return Math.Clamp(score, 0, 100);
    }

    public bool ValidateData(SensorData data, Sensor sensor)
    {
        if (data.IsFaulty || data.IsSpike)
            return false;

        // Additional validation: Ensure temperature is properly formatted with 2 decimal places
        var roundedTemperature = Math.Round(data.Temperature, 2);
        if (roundedTemperature != data.Temperature)
        {
            _logger.LogWarning("Temperature value {Temperature} is not properly rounded to 2 decimal places for sensor {SensorName}",
                data.Temperature, data.SensorName);
            return false;
        }

        // Primary validation: Fixed 22-24°C range from configuration
        bool isValidByFixedRange = data.Temperature >= _fixedRange.Min && data.Temperature <= _fixedRange.Max;

        // Secondary validation: Sensor-specific range
        bool isValidBySensorRange = data.Temperature >= sensor.MinValue && data.Temperature <= sensor.MaxValue;

        // Use fixed range as primary if configured, otherwise use sensor range
        if (_fixedRange.UseFixedRangeAsPrimary)
        {
            return isValidByFixedRange;
        }
        else
        {
            return isValidBySensorRange;
        }
    }

    public decimal SmoothData(List<SensorData> dataHistory)
    {
        if (dataHistory.Count == 0) return 0;

        var validData = dataHistory
            .Where(d => !d.IsFaulty && !d.IsSpike && d.IsValid)
            .Select(d => d.Temperature)
            .ToList();

        if (validData.Count == 0) return 0;

        return Math.Round(validData.Average(), 2);
    }

    public bool DetectAnomaly(SensorData currentData, List<SensorData> recentData)
    {
        if (currentData.IsFaulty || currentData.IsSpike)
            return true;

        var validRecentData = recentData
            .Where(d => !d.IsFaulty && !d.IsSpike && d.IsValid)
            .Select(d => d.Temperature)
            .ToList();

        if (validRecentData.Count < 5) return false;

        var average = validRecentData.Average();
        var variance = validRecentData.Average(v => (v - average) * (v - average));
        var stdDev = (decimal)Math.Sqrt((double)variance);

        return Math.Abs(currentData.Temperature - average) > 2 * stdDev;
    }

    public bool CheckThreshold(SensorData data, Sensor sensor, decimal? customMin = null, decimal? customMax = null)
    {
        decimal min, max;

        if (_fixedRange.UseFixedRangeAsPrimary)
        {
            min = _fixedRange.Min;
            max = _fixedRange.Max;
        }
        else
        {
            min = customMin ?? sensor.NormalMin;
            max = customMax ?? sensor.NormalMax;
        }

        return data.Temperature < min || data.Temperature > max;
    }

    public void InjectFault(Sensor sensor)
    {
        sensor.IsFaulty = true;
        _logger.LogWarning("Manually injected fault into sensor: {SensorName}", sensor.Name);
    }

    public void ClearFault(Sensor sensor)
    {
        sensor.IsFaulty = false;
        _logger.LogInformation("Cleared fault from sensor: {SensorName}", sensor.Name);
    }

    public void ShutdownSensor(Sensor sensor)
    {
        sensor.IsFaulty = true;
        _logger.LogInformation("Sensor {SensorName} has been shut down", sensor.Name);
    }

    public void StartSensor(Sensor sensor)
    {
        sensor.IsFaulty = false;
        _logger.LogInformation("Sensor {SensorName} has been started", sensor.Name);
    }
}