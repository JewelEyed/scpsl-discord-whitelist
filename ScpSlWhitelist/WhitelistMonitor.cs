using System.Collections.Concurrent;
using System.IO;
using System.Threading;

using LabApi.Features.Console;

namespace ScpSlWhitelist;


public sealed class WhitelistMonitor : IDisposable
{
    private readonly string _path;
    private readonly Timer _timer;
    private DateTime _lastWrite;

    private HashSet<string> _current;

    public WhitelistMonitor(string path, Action? onKickQueued = null)
    {
        _path = path;
        _onKickQueued = onKickQueued;
        _current = Whitelist.Load(path);
        _lastWrite = GetLastWrite();
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public HashSet<string> Current => Volatile.Read(ref _current);


    public ConcurrentQueue<string> PendingKicks { get; } = new();

    private readonly Action? _onKickQueued;

    private void Poll()
    {
        try
        {
            var write = GetLastWrite();
            if (write == _lastWrite)
                return;

            _lastWrite = write;

            var updated = Whitelist.Load(_path);
            var previous = Interlocked.Exchange(ref _current, updated);

            foreach (var id in updated)
                if (!previous.Contains(id))
                    Logger.Info($"Whitelist entry added: {id}");

            foreach (var id in previous)
                if (!updated.Contains(id))
                {
                    Logger.Info($"Whitelist entry removed: {id}");
                    PendingKicks.Enqueue(id);
                }

            if (PendingKicks.Count > 0)
                _onKickQueued?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error($"Whitelist monitor failed: {ex.Message}");
        }
    }

    private DateTime GetLastWrite() =>
        File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;

    public void Dispose() => _timer.Dispose();
}
