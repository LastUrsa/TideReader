namespace TideReader.Backend.Services;

public sealed class AppLogger : IDisposable
{
    private const int MaxRecentLines = 120;
    private StreamWriter _writer;
    private readonly Lock _lock = new();
    private readonly Queue<string> _recentLines = new();
    private readonly string _logDir;
    private readonly long _maxBytes;
    private readonly int _maxArchives;

    public AppLogger(string? logDir = null, long maxBytes = 1_048_576, int maxArchives = 5)
    {
        _logDir = logDir ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TideReader", "logs");
        _maxBytes = maxBytes;
        _maxArchives = maxArchives;
        Directory.CreateDirectory(_logDir);
        Path = System.IO.Path.Combine(_logDir, "bridge.log");
        RotateIfNeeded();
        _writer = new StreamWriter(File.Open(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public string Path { get; }
    public string DirectoryPath => _logDir;

    public void Info(string message)
    {
        lock (_lock)
        {
            RotateIfNeeded();
            var line = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss.fffffff} {message}";
            _writer.WriteLine(line);
            _recentLines.Enqueue(line);
            while (_recentLines.Count > MaxRecentLines)
            {
                _recentLines.Dequeue();
            }
        }
    }

    public string[] GetRecentLines()
    {
        lock (_lock)
        {
            return _recentLines.ToArray();
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    private void RotateIfNeeded()
    {
        var fileInfo = new FileInfo(Path);
        if (!fileInfo.Exists || fileInfo.Length < _maxBytes)
        {
            return;
        }

        _writer?.Flush();
        _writer?.Dispose();

        for (var index = _maxArchives; index >= 1; index--)
        {
            var source = index == 1 ? Path : $"{Path}.{index - 1}";
            var destination = $"{Path}.{index}";
            if (!File.Exists(source))
            {
                continue;
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }

        var replacement = new StreamWriter(File.Open(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        _writer = replacement;
    }
}
