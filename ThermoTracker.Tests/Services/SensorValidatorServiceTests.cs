using System.Reflection;
using ThermoTracker.ThermoTracker.Models;
using ThermoTracker.ThermoTracker.Services;

namespace ThermoTracker.ThermoTracker.Tests.Services;

public class SensorValidatorServiceTests
{
    private static void InvokeValidation(SensorConfig config)
    {
        // Use reflection to call the private static method
        typeof(SensorValidatorService)
            .GetMethod("ValidateSensorConfig", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, new object[] { config });
    }

    [Fact]
    public void ValidateSensorConfig_ValidConfig_Passes()
    {
        var config = new SensorConfig
        {
            Name = "TemperatureSensor",
            Location = "Lab1",
            MinValue = 10,
            MaxValue = 50,
            NormalMin = 20,
            NormalMax = 30,
            FaultProbability = 0.01M,
            SpikeProbability = 0.005M
        };

        var ex = Record.Exception(() => InvokeValidation(config));
        Assert.Null(ex); // no exception expected
    }

    [Theory]
    [InlineData("", "Lab1", 10, 50, 20, 30, 0.01, 0.005, "Sensor name cannot be empty")]
    [InlineData("SensorA", "", 10, 50, 20, 30, 0.01, 0.005, "Sensor location cannot be empty")]
    [InlineData("SensorA", "Lab1", 50, 10, 20, 30, 0.01, 0.005, "MinValue must be less than MaxValue")]
    [InlineData("SensorA", "Lab1", 10, 50, 5, 30, 0.01, 0.005, "Normal range must be within min/max range")]
    [InlineData("SensorA", "Lab1", 10, 50, 20, 60, 0.01, 0.005, "Normal range must be within min/max range")]
    [InlineData("SensorA", "Lab1", 10, 50, 20, 30, -0.1, 0.005, "Fault probability must be between 0 and 1")]
    [InlineData("SensorA", "Lab1", 10, 50, 20, 30, 1.5, 0.005, "Fault probability must be between 0 and 1")]
    [InlineData("SensorA", "Lab1", 10, 50, 20, 30, 0.01, -0.1, "Spike probability must be between 0 and 1")]
    [InlineData("SensorA", "Lab1", 10, 50, 20, 30, 0.01, 1.5, "Spike probability must be between 0 and 1")]
    public void ValidateSensorConfig_InvalidConfig_ThrowsArgumentException(
        string name,
        string location,
        decimal minValue,
        decimal maxValue,
        decimal normalMin,
        decimal normalMax,
        decimal faultProbability,
        decimal spikeProbability,
        string expectedMessage)
    {
        var config = new SensorConfig
        {
            Name = name,
            Location = location,
            MinValue = minValue,
            MaxValue = maxValue,
            NormalMin = normalMin,
            NormalMax = normalMax,
            FaultProbability = faultProbability,
            SpikeProbability = spikeProbability
        };

        var ex = Assert.Throws<TargetInvocationException>(() => InvokeValidation(config));
        Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Equal(expectedMessage, ex.InnerException.Message);
    }
}