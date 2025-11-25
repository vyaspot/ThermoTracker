using Microsoft.Extensions.Logging;
using ThermoTracker.ThermoTracker.Helpers;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public class SensorConfigWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ILogger<SensorConfigWatcher> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _filePath;

    public event Action<List<SensorConfig>>? OnConfigChanged;


    public SensorConfigWatcher(
        ILogger<SensorConfigWatcher> logger,
        IServiceProvider serviceProvider,
        string filePath = "sensors.yml")
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _filePath = filePath;

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var file = Path.GetFileName(fullPath);

        _watcher = new FileSystemWatcher(directory, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.FileName | NotifyFilters.Attributes
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.EnableRaisingEvents = true;

        logger.LogInformation("Watching {File} for changes...", fullPath);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Prevent duplicate events
        Thread.Sleep(200);

        try
        {
            _logger.LogInformation("Detected change in {File}. Reloading...", _filePath);

            var configs = YamlConfigurationHelper.LoadSensorConfigs(_filePath);

            OnConfigChanged?.Invoke(configs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload sensor configuration.");
        }
    }

    public void Dispose() => _watcher.Dispose();
}
