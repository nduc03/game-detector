namespace GameProcessChecker;

/// <summary>Vòng lặp nền: 30 giây quét 1 lần, ghi file sensor khi trạng thái đổi.</summary>
internal sealed class GameWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly TargetConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool? _lastState;

    public event Action<bool>? StateChanged;

    public GameWatcher(TargetConfig config) => _config = config;

    public void Start() => _loop = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                Tick();
            }
            while (await timer.WaitForNextTickAsync(_cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Đang thoát app.
        }
    }

    private void Tick()
    {
        try
        {
            bool running = ProcessScanner.IsAnyTargetRunning(_config.GetCurrent());

            // Chỉ ghi khi trạng thái đổi hoặc file bị xóa => gần như không tốn I/O.
            if (_lastState != running || !SensorFile.Exists())
            {
                SensorFile.Write(running);
            }

            if (_lastState != running)
            {
                _lastState = running;
                StateChanged?.Invoke(running);
            }
        }
        catch
        {
            // Lỗi tạm thời không được làm chết vòng lặp nền.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }

        // Thoát app thì không còn ai giám sát nữa => trả về "0" để tránh trạng thái "1" bị kẹt.
        try { SensorFile.Write(false); } catch { }

        _cts.Dispose();
    }
}
