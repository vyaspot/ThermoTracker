using Microsoft.Extensions.Logging;
using Moq;
using ThermoTracker.ThermoTracker.Models;
using ThermoTracker.ThermoTracker.Services;

namespace ThermoTracker.ThermoTracker.Tests.Services;

public class SensorConfigWatcherTests
{
    [Fact]
    public void OnConfigChanged_IsRaised_WhenFileIsModified()
    {
        // Arrange: create temp directory + file
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "sensors.yml");

        // Write valid YAML with "sensors" key
        File.WriteAllText(filePath,
            @"sensors:
              - name: TestSensor
                location: Lab
                minValue: 0
                maxValue: 100
                normalMin: 22
                normalMax: 24
                noiseRange: 0.5
                faultProbability: 0.01
                spikeProbability: 0.005");

        var loggerMock = new Mock<ILogger<SensorConfigWatcher>>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        var watcher = new SensorConfigWatcher(loggerMock.Object, serviceProviderMock.Object, filePath);

        var resetEvent = new ManualResetEventSlim(false);
        List<SensorConfig>? receivedConfigs = null;

        watcher.OnConfigChanged += configs =>
        {
            receivedConfigs = configs;
            resetEvent.Set(); // signal that event fired
        };

        // Act: modify file to trigger FileSystemWatcher
        File.AppendAllText(filePath, Environment.NewLine + "# change");

        // Wait up to 5 seconds for event
        var eventFired = resetEvent.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(eventFired, "FileSystemWatcher did not raise event in time.");
        Assert.NotNull(receivedConfigs);
        Assert.Single(receivedConfigs);
        Assert.Equal("TestSensor", receivedConfigs[0].Name);
        Assert.Equal("Lab", receivedConfigs[0].Location);

        // Cleanup
        watcher.Dispose();
        Directory.Delete(tempDir, true);
    }
}