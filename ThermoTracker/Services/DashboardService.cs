using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using ThermoTracker.ThermoTracker.Configurations;
using ThermoTracker.ThermoTracker.Data;
using ThermoTracker.ThermoTracker.Enums;
using ThermoTracker.ThermoTracker.Models;

namespace ThermoTracker.ThermoTracker.Services;

public class DashboardService : IHostedService
{
    private readonly ISensorService _sensorService;
    private readonly IDataService _dataService;
    private readonly ILogger<DashboardService> _logger;
    private Timer? _timer;
    private List<Sensor> _sensors = [];
    private readonly Dictionary<string, List<SensorData>> _dataHistory = [];
    private readonly SimulationSettings _simulationSettings;
    private readonly TemperatureRangeSettings _fixedRange;
    private readonly FileLoggingSettings _fileLoggingSettings;
    private readonly SensorDbContext _sensorDb;
    private readonly object _lock = new();


    public DashboardService(
    ISensorService sensorService,
    IDataService dataService,
    SensorConfigWatcher configWatcher,
    IOptions<FileLoggingSettings> fileLoggingOptions,
    IOptions<SimulationSettings> simulationOptions,
    IOptions<TemperatureRangeSettings> fixedRangeOptions,
    SensorDbContext sensorDb,
    ILogger<DashboardService> logger)
    {
        _sensorService = sensorService;
        _dataService = dataService;
        _logger = logger;
        _simulationSettings = simulationOptions.Value;
        _fixedRange = fixedRangeOptions.Value;
        _fileLoggingSettings = fileLoggingOptions.Value;
        _sensorDb = sensorDb;

        configWatcher.OnConfigChanged += configs =>
        {
            lock (_lock)
            {
                var cancellationToken = new CancellationToken();
                _ = StartAsync(cancellationToken);
                _logger.LogInformation("Sensor configurations updated. {Count} sensors loaded.", _sensors.Count);
            }
        };

    }


    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _sensors = _sensorService.GetSensors();

            foreach (var sensor in _sensors)
            {
                _dataHistory[sensor.Name] = new List<SensorData>();
            }

            _timer = new Timer(UpdateDashboard, null, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(_simulationSettings.UpdateIntervalMs));

            _logger.LogInformation("Dashboard service started with {SensorCount} sensors", _sensors.Count);
            _logger.LogInformation("Fixed temperature validation range: {Min}-{Max}°C",
                _fixedRange.Min, _fixedRange.Max);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start dashboard service");
            throw;
        }
    }

    private async void UpdateDashboard(object? state)
    {
        try
        {
            foreach (var sensor in _sensors)
            {
                var data = _sensorService.SimulateData(sensor);

                // Get recent data for smoothing and anomaly detection
                var recentData = await _dataService.GetRecentDataAsync(sensor.Id, 10);
                data.SmoothedValue = _sensorService.SmoothData(recentData);
                data.IsAnomaly = _sensorService.DetectAnomaly(data, recentData);

                await _dataService.StoreDataAsync(data);

                // Update history
                _dataHistory[sensor.Name].Add(data);
                if (_dataHistory[sensor.Name].Count > _simulationSettings.DataHistorySize)
                {
                    _dataHistory[sensor.Name].RemoveAt(0);
                }
            }

            DisplayDashboard();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating dashboard");
        }
    }

    private void DisplayDashboard()
    {
        AnsiConsole.Clear();

        // Header
        AnsiConsole.Write(new Rule("[bold HotPink3]🌡️  Temperature Sensor Dashboard[/]"));
        AnsiConsole.MarkupLine($"[bold indianRed]Fixed Validation Range: {_fixedRange.Min}-{_fixedRange.Max}°C[/]");
        AnsiConsole.WriteLine();

        // Sensor Table
        var sensorTable = CreateSensorTable();
        AnsiConsole.Write(sensorTable);
        AnsiConsole.WriteLine();

        // Statistics
        var statsPanel = CreateStatisticsPanel();
        AnsiConsole.Write(statsPanel);
        AnsiConsole.WriteLine();

        // File Logging Status
        var fileLoggingPanel = CreateFileLoggingPanel();
        AnsiConsole.Write(fileLoggingPanel);
        AnsiConsole.WriteLine();

        // Footer
        AnsiConsole.Write(new Rule("[grey]Press Ctrl+C to exit[/]"));
    }

    private Table CreateSensorTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.LightYellow3)
            .Title("[bold gray]Live Sensor Data[/]")
            .AddColumn(new TableColumn("[bold LightSlateGrey]Sensor[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold LightSlateGrey]Location[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold LightSlateGrey]Temperature[/]").Centered())
            .AddColumn(new TableColumn("[bold LightSlateGrey]Status[/]").Centered())
            .AddColumn(new TableColumn("[bold LightSlateGrey]Smoothed[/]").Centered())
            .AddColumn(new TableColumn("[bold LightSlateGrey]Alerts[/]").Centered());

        foreach (var sensor in _sensors)
        {
            if (sensor.Status.Equals("Offline")) continue;

            var history = _dataHistory[sensor.Name];
            var latestData = history.LastOrDefault();

            if (latestData == null) continue;

            var temperatureDisplay = $"{latestData.Temperature:F2}°C";
            var smoothedDisplay = $"{latestData.SmoothedValue:F2}°C";

            var tempColor = GetTemperatureStyle(latestData.Temperature);
            var status = latestData.IsValid ?
                new Markup("[green] VALID[/]") :
                new Markup("[red] INVALID[/]");

            var alertText = latestData.AlertType switch
            {
                AlertType.None => new Markup("[green]NORMAL[/]"),
                AlertType.Threshold => new Markup("[orange1]THRESHOLD[/]"),
                AlertType.Anomaly => new Markup("[orange3]ANOMALY[/]"),
                AlertType.Spike => new Markup("[darkorange]SPIKE[/]"),
                AlertType.Fault => new Markup("[red]FAULT[/]"),
                AlertType.Offline => new Markup("[grey]OFFLINE[/]"),
                _ => new Markup("[white]UNKNOWN[/]")
            };

            table.AddRow(
                new Markup($"{sensor.Name}"),
                new Markup($"{sensor.Location}"),
                new Markup($"[{tempColor}]{temperatureDisplay}[/]"),
                status,
                new Markup($"[cyan]{smoothedDisplay}[/]"),
                alertText
            );
        }

        return table;
    }

    private static string GetTemperatureStyle(decimal temperature)
    {
        return temperature switch
        {
            < 20 => "blue",
            < 22 => "cyan",
            >= 22 and <= 24 => "green",
            > 24 and <= 26 => "orange1",
            > 26 => "red"
        };
    }

    private Panel CreateStatisticsPanel()
    {
        var totalReadings = _sensors.Sum(s => _dataHistory[s.Name].Count);
        var validReadings = _sensors.Sum(s => _dataHistory[s.Name].Count(d => d.IsValid));
        var anomalyReadings = _sensors.Sum(s => _dataHistory[s.Name].Count(d => d.IsAnomaly));
        var spikeReadings = _sensors.Sum(s => _dataHistory[s.Name].Count(d => d.IsSpike));
        var faultReadings = _sensors.Sum(s => _dataHistory[s.Name].Count(d => d.IsFaulty));

        var validityRate = totalReadings > 0 ? (double)validReadings / totalReadings * 100 : 0;

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(
            new Markup($"[blue]Total: {totalReadings}[/]"),
            new Markup($"[green]Valid: {validReadings} ({validityRate:F1}%)[/]"),
            new Markup($"[red]Anomalies: {anomalyReadings}[/]"),
            new Markup($"[orange1]Spikes: {spikeReadings}[/]")
        );

        grid.AddRow(
            new Markup($"[purple]Faults: {faultReadings}[/]"),
            new Markup($"[grey]Sensors: {_sensors.Count}[/]"),
            new Markup($"[grey]Update: {DateTime.Now:HH:mm:ss}[/]"),
            new Markup($"[grey]Fixed Range: {_fixedRange.Min}-{_fixedRange.Max}°C[/]")
        );

        return new Panel(grid)
        {
            Header = new PanelHeader("[bold gray]📊 Statistics [/]"),

            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.HotPink3)
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _logger.LogInformation("Dashboard service stopped");
        return Task.CompletedTask;
    }

    private Panel CreateFileLoggingPanel()
    {
        try
        {
            var loggingInfo = _dataService.GetFileLoggingInfoAsync().GetAwaiter().GetResult();
            var currentLogPath = loggingInfo.CurrentLogFilePath;
            var fileSize = loggingInfo.CurrentLogFileSizeBytes;
            var entryCount = loggingInfo.CurrentFileEntryCount;

            var grid = new Grid();
            grid.AddColumn();
            grid.AddColumn();

            grid.AddRow(
                new Markup($"[blue]Current File: {Path.GetFileName(currentLogPath)}[/]"),
                new Markup($"[green]Size: {(fileSize / 1024.0):F1} KB[/]")
            );

            grid.AddRow(
                new Markup($"[IndianRed]Entries: {entryCount} records[/]"),
                new Markup($"[grey]Total Files: {loggingInfo.TotalLogFiles}[/]")
            );

            grid.AddRow(
                new Markup($"[cyan]Directory: {_fileLoggingSettings.LogDirectory}[/]"),
                new Markup($"[orange1]Status: {(entryCount > 0 ? "ACTIVE" : "NO DATA")}[/]")
            );

            var statusColor = entryCount > 0 ? Color.Green : Color.Red;
            var statusText = entryCount > 0 ? "ACTIVE" : "No Data Written";

            return new Panel(grid)
            {
                Header = new PanelHeader($"[bold gray]📁 File Logging[/] "),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(statusColor)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file logging info for dashboard");

            // Return error panel
            var errorGrid = new Grid();
            errorGrid.AddColumn();
            errorGrid.AddRow(new Markup($"[red]Error getting file logging status: {ex.Message}[/]"));

            return new Panel(errorGrid)
            {
                Header = new PanelHeader("📁 File Logging Status"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Red)
            };
        }
    }
}