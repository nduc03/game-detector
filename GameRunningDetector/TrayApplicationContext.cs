namespace GameProcessChecker;

/// <summary>App không có form: chỉ có tray icon + menu chuột phải "Thoát".</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly GameWatcher _watcher;
    private readonly SynchronizationContext _ui;

    public TrayApplicationContext(string[] args)
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Thoát", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Game Process Checker",
            ContextMenuStrip = menu,
            Visible = true
        };

        _watcher = new GameWatcher(new TargetConfig(args));
        _watcher.StateChanged += OnStateChanged;
        _watcher.Start();
    }

    private void OnStateChanged(bool running)
    {
        // NotifyIcon.Text tối đa 63 ký tự.
        string text = running ? "Game Process Checker: đang có game" : "Game Process Checker: không có game";
        _ui.Post(_ => _tray.Text = text, null);
    }

    protected override void ExitThreadCore()
    {
        _watcher.StateChanged -= OnStateChanged;
        _watcher.Dispose();

        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();

        base.ExitThreadCore();
    }
}
