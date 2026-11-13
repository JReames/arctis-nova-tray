using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;

namespace ArctisNovaTray.Ui;

public sealed class TrayApplicationContext : ApplicationContext
{
    public const string ShutdownEventName = @"Local\ArctisNovaTray_Shutdown_v1";
    public const string ShowCardEventName = @"Local\ArctisNovaTray_ShowCard_v1";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan HotplugDebounce = TimeSpan.FromMilliseconds(350);

    private readonly IHeadsetProbe _probe;
    private readonly SynchronizationContext _uiContext;
    private readonly TrayIconCache _iconCache = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _summaryMenuItem;
    private readonly StatusFlyoutForm _flyout;
    private readonly DeviceChangeMonitor _deviceMonitor;
    private readonly EventWaitHandle _shutdownEvent;
    private readonly RegisteredWaitHandle _shutdownWaitRegistration;
    private readonly EventWaitHandle _showCardEvent;
    private readonly RegisteredWaitHandle _showCardWaitRegistration;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    private CancellationTokenSource? _debounceCts;
    private HeadsetSnapshot _latestSnapshot = HeadsetSnapshot.CreateInitial();
    private TrayVisualKey? _appliedVisualKey;
    private int _appliedIconSize;
    private bool _showCardOnFirstProbe;
    private bool _disposed;

    public TrayApplicationContext(IHeadsetProbe probe, bool showCardOnStartup = true)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _showCardOnFirstProbe = showCardOnStartup;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _summaryMenuItem = new ToolStripMenuItem("Status: Checking...")
        {
            Enabled = false
        };

        var openCardItem = new ToolStripMenuItem("Open Status Card")
        {
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
        };
        openCardItem.Click += (_, _) => ShowStatusCardAtCursor();

        var refreshItem = new ToolStripMenuItem("Refresh Now");
        refreshItem.Click += (_, _) => _ = RefreshNowAsync();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        _contextMenu = new ContextMenuStrip
        {
            Renderer = new LightMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(255, 255, 255),
            ForeColor = Color.FromArgb(15, 23, 42)
        };
        _contextMenu.Items.AddRange(
        [
            _summaryMenuItem,
            new ToolStripSeparator(),
            openCardItem,
            refreshItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        int initialSize = TrayIconCache.GetSystemTrayIconSize();
        _appliedIconSize = initialSize;
        _appliedVisualKey = _latestSnapshot.VisualState;

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconCache.GetIcon(_latestSnapshot.VisualState, initialSize),
            Text = TooltipFormatter.Format(_latestSnapshot),
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;

        _flyout = new StatusFlyoutForm();
        _flyout.RefreshRequested += () => _ = RefreshNowAsync();

        _deviceMonitor = new DeviceChangeMonitor();
        _deviceMonitor.DeviceChanged += OnDeviceChanged;

        _shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
        _shutdownEvent.Reset();
        _shutdownWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_disposed)
                {
                    _uiContext.Post(_ => ExitThread(), null);
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: true);

        _showCardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowCardEventName);
        _showCardEvent.Reset();
        _showCardWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showCardEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_disposed)
                {
                    _uiContext.Post(_ => ShowStatusCardNearTrayAndRefresh(), null);
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        _ = RunPollLoopAsync(_cts.Token);
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_flyout.Visible)
        {
            _flyout.Hide();
            return;
        }

        if (_flyout.WasRecentlyDeactivated)
        {
            return;
        }

        ShowStatusCardAtCursor();
    }

    private void ShowStatusCardAtCursor()
    {
        _flyout.UpdateSnapshot(_latestSnapshot, clearRefreshing: false);
        _flyout.ShowNearPoint(Cursor.Position);
    }

    private void ShowStatusCardNearTrayAndRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _flyout.UpdateSnapshot(_latestSnapshot, clearRefreshing: false);
        _flyout.ShowNearTrayArea();
        _ = RefreshNowAsync();
    }

    private void OnDeviceChanged()
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource? previous = Interlocked.Exchange(ref _debounceCts, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        CancellationToken token = _debounceCts!.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(HotplugDebounce, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested && !_cts.IsCancellationRequested)
                {
                    await ExecuteSingleProbeAsync(_cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private Task RefreshNowAsync() =>
        Task.Run(() => ExecuteSingleProbeAsync(_cts.Token), CancellationToken.None);

    private async Task RunPollLoopAsync(CancellationToken cancellationToken)
    {
        await ExecuteSingleProbeAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await ExecuteSingleProbeAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ExecuteSingleProbeAsync(CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await _probeLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            HeadsetSnapshot snapshot = _probe.Probe();
            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                _uiContext.Post(state => ApplySnapshotOnUiThread((HeadsetSnapshot)state!), snapshot);
            }
        }
        catch (Exception ex)
        {
            var fallback = new HeadsetSnapshot
            {
                Timestamp = DateTimeOffset.Now,
                PcConnection = PcConnectionState.Disconnected,
                Bluetooth = BluetoothState.Off,
                DiagnosticNote = $"Probe error: {ex.Message}"
            };
            if (!_disposed)
            {
                _uiContext.Post(state => ApplySnapshotOnUiThread((HeadsetSnapshot)state!), fallback);
            }
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private void ApplySnapshotOnUiThread(HeadsetSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _latestSnapshot = snapshot;

        int targetSize = TrayIconCache.GetSystemTrayIconSize();
        TrayVisualKey targetKey = snapshot.VisualState;

        if (_appliedVisualKey != targetKey || _appliedIconSize != targetSize)
        {
            _notifyIcon.Icon = _iconCache.GetIcon(targetKey, targetSize);
            _appliedVisualKey = targetKey;
            _appliedIconSize = targetSize;
        }

        string tooltip = TooltipFormatter.Format(snapshot);
        if (!string.Equals(_notifyIcon.Text, tooltip, StringComparison.Ordinal))
        {
            _notifyIcon.Text = tooltip;
        }

        _summaryMenuItem.Text = TooltipFormatter.FormatMenuSummary(snapshot);
        _flyout.UpdateSnapshot(snapshot);

        if (_showCardOnFirstProbe)
        {
            _showCardOnFirstProbe = false;
            _flyout.ShowNearTrayArea();
        }
    }

    protected override void ExitThreadCore()
    {
        CleanupResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupResources();
        }

        base.Dispose(disposing);
    }

    private void CleanupResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        CancellationTokenSource? debounce = Interlocked.Exchange(ref _debounceCts, null);
        debounce?.Cancel();
        debounce?.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= OnNotifyIconMouseClick;

        _deviceMonitor.DeviceChanged -= OnDeviceChanged;
        _deviceMonitor.Dispose();

        _shutdownWaitRegistration.Unregister(null);
        _shutdownEvent.Dispose();

        _showCardWaitRegistration.Unregister(null);
        _showCardEvent.Dispose();

        _flyout.Dispose();
        _contextMenu.Dispose();
        _notifyIcon.Dispose();
        _iconCache.Dispose();
        _cts.Dispose();
        _probeLock.Dispose();
    }

    private sealed class LightMenuRenderer() : ToolStripProfessionalRenderer(new LightMenuColors())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? Color.FromArgb(15, 23, 42)    // #0F172A
                : Color.FromArgb(100, 116, 139); // #64748B
            base.OnRenderItemText(e);
        }
    }

    private sealed class LightMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(255, 255, 255); // #FFFFFF
        public override Color MenuBorder => Color.FromArgb(203, 213, 225);                  // #CBD5E1
        public override Color MenuItemBorder => Color.FromArgb(203, 213, 225);              // #CBD5E1
        public override Color MenuItemSelected => Color.FromArgb(241, 245, 249);            // #F1F5F9
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(241, 245, 249);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(241, 245, 249);
        public override Color SeparatorDark => Color.FromArgb(226, 232, 240);               // #E2E8F0
        public override Color SeparatorLight => Color.FromArgb(226, 232, 240);
        public override Color ImageMarginGradientBegin => Color.FromArgb(255, 255, 255);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(255, 255, 255);
        public override Color ImageMarginGradientEnd => Color.FromArgb(255, 255, 255);
    }
}
