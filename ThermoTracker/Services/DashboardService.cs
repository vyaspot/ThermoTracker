using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using ThermoTracker.ThermoTracker.Configurations;
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

    public DashboardService(
        ISensorService sensorService,
        IDataService dataService,
        IOptions<SimulationSettings> simulationOptions,
        IOptions<TemperatureRangeSettings> fixedRangeOptions,
        ILogger<DashboardService> logger)
    {
        _sensorService = sensorService;
        _dataService = dataService;
        _logger = logger;
        _simulationSettings = simulationOptions.Value;
        _fixedRange = fixedRangeOptions.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _sensors = _sensorService.InitializeSensors();

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

        return Task.CompletedTask;
    }

    private async void UpdateDashboard(object? state)
    {
        try
        {
            foreach (var sensor in _sensors)
            {
                var data = _sensorService.SimulateData(sensor);

                // Get recent data for smoothing and anomaly detection
                var recentData = await _dataService.GetRecentDataAsync(sensor.Name, 10);
                data.SmoothedValue = _sensorService.SmoothData(recentData);
                data.IsAnomaly = _sensorService.DetectAnomaly(data, recentData);

                // Store and log data
                await _dataService.StoreDataAsync(data);
                //await _dataService.LogDataAsync(data);

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
        AnsiConsole.Write(new Rule("[yellow]🌡️ Temperature Sensor Dashboard[/]"));
        AnsiConsole.MarkupLine($"[grey]Fixed Validation Range: {_fixedRange.Min}-{_fixedRange.Max}°C[/]");
        AnsiConsole.WriteLine();

        // Sensor Table
        var sensorTable = CreateSensorTable();
        AnsiConsole.Write(sensorTable);
        AnsiConsole.WriteLine();

        // Statistics
        var statsPanel = CreateStatisticsPanel();
        AnsiConsole.Write(statsPanel);
        AnsiConsole.WriteLine();

        // Footer
        AnsiConsole.Write(new Rule("[grey]Press Ctrl+C to exit[/]"));
    }

    private Table CreateSensorTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold yellow]Live Sensor Data[/]")
            .AddColumn(new TableColumn("[yellow]Sensor[/]").LeftAligned())
            .AddColumn(new TableColumn("[yellow]Location[/]").LeftAligned())
            .AddColumn(new TableColumn("[yellow]Temperature[/]").Centered())
            .AddColumn(new TableColumn("[yellow]Status[/]").Centered())
            .AddColumn(new TableColumn("[yellow]Smoothed[/]").Centered())
            .AddColumn(new TableColumn("[yellow]Alerts[/]").Centered());

        foreach (var sensor in _sensors)
        {
            var history = _dataHistory[sensor.Name];
            var latestData = history.LastOrDefault();

            if (latestData == null) continue;

            var tempColor = GetTemperatureStyle(latestData.Temperature);
            var status = latestData.IsValid ?
                new Markup("[green] VALID[/]") :
                new Markup("[red] INVALID[/]");

            var alertText = latestData.AlertType switch
            {
                AlertType.None => new Markup("[green]NORMAL[/]"),
                AlertType.Threshold => new Markup("[yellow]THRESHOLD[/]"),
                AlertType.Anomaly => new Markup("[orange3]ANOMALY[/]"),
                AlertType.Spike => new Markup("[darkorange]SPIKE[/]"),
                AlertType.Fault => new Markup("[red]FAULT[/]"),
                AlertType.Offline => new Markup("[grey]OFFLINE[/]"),
                _ => new Markup("[white]UNKNOWN[/]")
            };

            table.AddRow(
                new Markup($"{sensor.Name}"),
                new Markup($"{sensor.Location}"),
                new Markup($"[{tempColor}]{latestData.Temperature}°C[/]"),
                status,
                new Markup($"[cyan]{latestData.SmoothedValue}°C[/]"),
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
            > 24 and <= 26 => "yellow",
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
            new Markup($"[yellow]Spikes: {spikeReadings}[/]")
        );

        grid.AddRow(
            new Markup($"[purple]Faults: {faultReadings}[/]"),
            new Markup($"[grey]Sensors: {_sensors.Count}[/]"),
            new Markup($"[grey]Update: {DateTime.Now:HH:mm:ss}[/]"),
            new Markup($"[grey]Fixed Range: {_fixedRange.Min}-{_fixedRange.Max}°C[/]")
        );

        return new Panel(grid)
        {
            Header = new PanelHeader("📊 Statistics"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _logger.LogInformation("Dashboard service stopped");
        return Task.CompletedTask;
    }
}