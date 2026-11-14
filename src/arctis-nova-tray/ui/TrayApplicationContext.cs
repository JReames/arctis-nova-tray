using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;

namespace ArctisNovaTray.Ui;

public sealed class TrayApplicationContext : ApplicationContext
{
    public const string ShutdownEventName = @"Local\ArctisNovaTray_Shutdown_v1";
    public const string ShowCardEventName = @"Local\ArctisNovaTray_ShowCard_v1";

    public static readonly Color MenuSurfaceColor = Color.FromArgb(255, 255, 255);    // #FFFFFF
    public static readonly Color MenuOuterBorderColor = Color.FromArgb(100, 116, 139); // #64748B (4.76:1)
    public static readonly Color MenuHoverFillColor = Color.FromArgb(239, 246, 255);   // #EFF6FF
    public static readonly Color MenuHoverBorderColor = Color.FromArgb(37, 99, 235);   // #2563EB (4.9:1)
    public static readonly Color MenuSeparatorColor = Color.FromArgb(148, 163, 184);   // #94A3B8 (2.56:1)
    public static readonly Color MenuPrimaryInkColor = Color.FromArgb(15, 23, 42);     // #0F172A (18.2:1)
    public static readonly Color MenuHeaderInkColor = Color.FromArgb(51, 65, 85);      // #334155 (10.3:1)

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan HotplugDebounce = TimeSpan.FromMilliseconds(350);

    private readonly IHeadsetProbe _probe;
    private readonly SynchronizationContext _uiContext;
    private readonly TrayIconCache _iconCache = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Font _menuFont;
    private readonly Font _menuPrimaryFont;
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

        _menuFont = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _menuPrimaryFont = new Font("Segoe UI Semibold", 10f, FontStyle.Bold, GraphicsUnit.Point);
        var itemPadding = new Padding(12, 7, 12, 7);

        _summaryMenuItem = new ToolStripMenuItem("Checking headset status...")
        {
            Enabled = false,
            Font = _menuFont,
            Padding = itemPadding
        };

        var openCardItem = new ToolStripMenuItem("Open Status Card")
        {
            Font = _menuPrimaryFont,
            Padding = itemPadding
        };
        openCardItem.Click += (_, _) => ShowStatusCardAtCursor();

        var refreshItem = new ToolStripMenuItem("Refresh Now")
        {
            Font = _menuFont,
            Padding = itemPadding
        };
        refreshItem.Click += (_, _) => _ = RefreshNowAsync(isManual: true);

        var exitItem = new ToolStripMenuItem("Exit")
        {
            Font = _menuFont,
            Padding = itemPadding
        };
        exitItem.Click += (_, _) => ExitThread();

        _contextMenu = new ContextMenuStrip
        {
            Renderer = new LightMenuRenderer(),
            ShowImageMargin = false,
            Font = _menuFont,
            Padding = new Padding(4, 5, 4, 5),
            BackColor = MenuSurfaceColor,
            ForeColor = MenuPrimaryInkColor
        };
        _contextMenu.Items.AddRange(
        [
            _summaryMenuItem,
            new ToolStripSeparator { Margin = new Padding(8, 3, 8, 3) },
            openCardItem,
            refreshItem,
            new ToolStripSeparator { Margin = new Padding(8, 3, 8, 3) },
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
        _flyout.RefreshRequested += () => _ = RefreshNowAsync(isManual: true);

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
        _flyout.UpdateSnapshot(_latestSnapshot, isManualRefreshCompletion: false);
        _flyout.ShowNearPoint(Cursor.Position);
    }

    private void ShowStatusCardNearTrayAndRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _flyout.UpdateSnapshot(_latestSnapshot, isManualRefreshCompletion: false);
        _flyout.ShowNearTrayArea();
        _ = RefreshNowAsync(isManual: false);
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
                    await ExecuteSingleProbeAsync(_cts.Token, isManual: false).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private Task RefreshNowAsync(bool isManual) =>
        Task.Run(() => ExecuteSingleProbeAsync(_cts.Token, isManual), CancellationToken.None);

    private async Task RunPollLoopAsync(CancellationToken cancellationToken)
    {
        await ExecuteSingleProbeAsync(cancellationToken, isManual: false).ConfigureAwait(false);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await ExecuteSingleProbeAsync(cancellationToken, isManual: false).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record ProbeCompletion(HeadsetSnapshot Snapshot, bool IsManual);

    private async Task ExecuteSingleProbeAsync(CancellationToken cancellationToken, bool isManual)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (isManual)
        {
            await _probeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await _probeLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            HeadsetSnapshot snapshot = _probe.Probe();
            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                _uiContext.Post(
                    state => ApplySnapshotOnUiThread((ProbeCompletion)state!),
                    new ProbeCompletion(snapshot, isManual));
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
                _uiContext.Post(
                    state => ApplySnapshotOnUiThread((ProbeCompletion)state!),
                    new ProbeCompletion(fallback, isManual));
            }
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private void ApplySnapshotOnUiThread(ProbeCompletion completion)
    {
        if (_disposed)
        {
            return;
        }

        HeadsetSnapshot snapshot = completion.Snapshot;
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
        _flyout.UpdateSnapshot(snapshot, isManualRefreshCompletion: completion.IsManual);

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
        _menuPrimaryFont.Dispose();
        _menuFont.Dispose();
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
                ? MenuPrimaryInkColor   // #0F172A (18.2:1 AAA)
                : MenuHeaderInkColor;   // #334155 (10.3:1 AAA, crisp readable header)
            base.OnRenderItemText(e);
        }
    }

    private sealed class LightMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => MenuSurfaceColor;
        public override Color MenuBorder => MenuOuterBorderColor;
        public override Color MenuItemBorder => MenuHoverBorderColor;
        public override Color MenuItemSelected => MenuHoverFillColor;
        public override Color MenuItemSelectedGradientBegin => MenuHoverFillColor;
        public override Color MenuItemSelectedGradientEnd => MenuHoverFillColor;
        public override Color SeparatorDark => MenuSeparatorColor;
        public override Color SeparatorLight => MenuSeparatorColor;
        public override Color ImageMarginGradientBegin => MenuSurfaceColor;
        public override Color ImageMarginGradientMiddle => MenuSurfaceColor;
        public override Color ImageMarginGradientEnd => MenuSurfaceColor;
    }
}
