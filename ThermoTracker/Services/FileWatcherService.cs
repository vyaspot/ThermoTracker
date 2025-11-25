namespace ThermoTracker.ThermoTracker.Services;

public class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Action _onConfigChanged;

    public FileWatcherService(string filePath, Action onConfigChanged)
    {
        _onConfigChanged = onConfigChanged ?? throw new ArgumentNullException(nameof(onConfigChanged));

        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;

        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: file writes often trigger multiple events
        System.Threading.Thread.Sleep(100);
        _onConfigChanged.Invoke();
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}