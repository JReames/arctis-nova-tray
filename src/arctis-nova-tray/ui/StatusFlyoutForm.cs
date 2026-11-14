using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;

namespace ArctisNovaTray.Ui;

public enum RefreshVisualState
{
    Normal,
    Hover,
    Pressed,
    Checking,
    Updated
}

public enum CardFocusTarget
{
    None,
    RefreshButton,
    CloseButton
}

public sealed partial class StatusFlyoutForm : Form
{
    public const int LogicalWidth = 436;
    public const int LogicalHeight = 392;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const long MinCheckingDurationMs = 420;
    private const long UpdatedConfirmationDurationMs = 1800;

    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly System.Windows.Forms.Timer _ageTickTimer;
    private readonly ToolTip _toolTip;

    private HeadsetSnapshot _snapshot = HeadsetSnapshot.CreateInitial();
    private HeadsetSnapshot? _bufferedManualSnapshot;

    private RectangleF _refreshButtonBounds;
    private RectangleF _closeButtonBounds;
    private RectangleF _subtitleBounds;

    private bool _refreshHovered;
    private bool _refreshPressed;
    private bool _closeHovered;
    private bool _closePressed;
    private string? _activeTooltipCaption;

    private RefreshVisualState _refreshPhase = RefreshVisualState.Normal;
    private CardFocusTarget _keyboardFocus = CardFocusTarget.None;
    private float _spinnerAngle;
    private long _checkingStartedTick;
    private long _confirmedUntilTick;
    private bool _manualProbeCompleted;
    private long _lastDeactivatedTick;

    public event Action? RefreshRequested;

    public StatusFlyoutForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(255, 255, 255);
        ForeColor = Color.FromArgb(15, 23, 42);
        AutoScaleMode = AutoScaleMode.None;
        AccessibleName = "Arctis Nova 7 Status Card";
        AccessibleRole = AccessibleRole.Dialog;

        _animTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _animTimer.Tick += OnAnimationTick;

        _ageTickTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _ageTickTimer.Tick += (_, _) =>
        {
            if (Visible)
            {
                Invalidate();
            }
        };

        _toolTip = new ToolTip
        {
            InitialDelay = 350,
            ReshowDelay = 150,
            ShowAlways = true
        };

        UpdateScaledSize();
    }

    public bool WasRecentlyDeactivated =>
        Environment.TickCount64 - Interlocked.Read(ref _lastDeactivatedTick) < 220;

    public void UpdateSnapshot(HeadsetSnapshot snapshot, bool isManualRefreshCompletion = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_refreshPhase == RefreshVisualState.Checking)
        {
            if (isManualRefreshCompletion)
            {
                _manualProbeCompleted = true;
                long elapsed = Environment.TickCount64 - _checkingStartedTick;
                if (elapsed >= MinCheckingDurationMs)
                {
                    _snapshot = snapshot;
                    _bufferedManualSnapshot = null;
                    TransitionToUpdatedConfirmation();
                }
                else
                {
                    _bufferedManualSnapshot = snapshot;
                }
            }
            else
            {
                _snapshot = snapshot;
            }
        }
        else
        {
            _snapshot = snapshot;
            if (isManualRefreshCompletion)
            {
                TransitionToUpdatedConfirmation();
            }
        }

        if (IsHandleCreated && Visible)
        {
            Invalidate();
        }
    }

    public void ShowNearPoint(Point anchorScreenPoint)
    {
        UpdateScaledSize();

        Rectangle workArea = Screen.FromPoint(anchorScreenPoint).WorkingArea;
        int margin = ScalePx(10);

        int x = anchorScreenPoint.X - (Width / 2);
        x = Math.Clamp(x, workArea.Left + margin, Math.Max(workArea.Left + margin, workArea.Right - Width - margin));

        int y = anchorScreenPoint.Y > (workArea.Top + workArea.Bottom) / 2
            ? workArea.Bottom - Height - margin
            : workArea.Top + margin;
        y = Math.Clamp(y, workArea.Top + margin, Math.Max(workArea.Top + margin, workArea.Bottom - Height - margin));

        Location = new Point(x, y);
        ShowAndActivate();
    }

    public void ShowNearTrayArea()
    {
        UpdateScaledSize();

        Screen? targetScreen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        Rectangle workArea = targetScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        int margin = ScalePx(12);

        int x = Math.Max(workArea.Left + margin, workArea.Right - Width - margin);
        int y = Math.Max(workArea.Top + margin, workArea.Bottom - Height - margin);

        Location = new Point(x, y);
        ShowAndActivate();
    }

    private void ShowAndActivate()
    {
        _keyboardFocus = CardFocusTarget.None;
        _refreshPressed = false;
        _closePressed = false;

        if (!Visible)
        {
            Show();
        }

        _ageTickTimer.Start();
        _ = SetForegroundWindow(Handle);
        BringToFront();
        Activate();
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _ageTickTimer.Stop();
            _refreshHovered = false;
            _refreshPressed = false;
            _closeHovered = false;
            _closePressed = false;
            Cursor = Cursors.Default;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int darkMode = 0;
        _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        int preference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateScaledSize();
        Invalidate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Interlocked.Exchange(ref _lastDeactivatedTick, Environment.TickCount64);
        Hide();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Hide();
                return true;

            case Keys.F5:
                TriggerManualRefresh();
                return true;

            case Keys.Tab:
                _keyboardFocus = _keyboardFocus == CardFocusTarget.RefreshButton
                    ? CardFocusTarget.CloseButton
                    : CardFocusTarget.RefreshButton;
                Invalidate();
                return true;

            case Keys.Tab | Keys.Shift:
                _keyboardFocus = _keyboardFocus == CardFocusTarget.CloseButton
                    ? CardFocusTarget.RefreshButton
                    : CardFocusTarget.CloseButton;
                Invalidate();
                return true;

            case Keys.Enter:
            case Keys.Space:
                if (_keyboardFocus == CardFocusTarget.CloseButton)
                {
                    Hide();
                }
                else
                {
                    _keyboardFocus = CardFocusTarget.RefreshButton;
                    TriggerManualRefresh();
                }
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool overRefresh = _refreshButtonBounds.Contains(e.Location);
        bool overClose = _closeButtonBounds.Contains(e.Location);
        bool overSub = _subtitleBounds.Contains(e.Location);

        if (overRefresh != _refreshHovered || overClose != _closeHovered)
        {
            _refreshHovered = overRefresh;
            _closeHovered = overClose;
            Cursor = (overRefresh || overClose) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        string? desiredTip = overClose
            ? "Close (Esc)"
            : overRefresh
                ? "Check headset status now (F5)"
                : overSub && !string.IsNullOrWhiteSpace(_snapshot.DongleVidPid)
                    ? $"USB Receiver ID: {_snapshot.DongleVidPid}"
                    : null;

        if (!string.Equals(_activeTooltipCaption, desiredTip, StringComparison.Ordinal))
        {
            _activeTooltipCaption = desiredTip;
            _toolTip.SetToolTip(this, desiredTip ?? string.Empty);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_refreshButtonBounds.Contains(e.Location))
        {
            _keyboardFocus = CardFocusTarget.None;
            _refreshPressed = true;
            Invalidate();
        }
        else if (_closeButtonBounds.Contains(e.Location))
        {
            _keyboardFocus = CardFocusTarget.None;
            _closePressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        bool wasRefreshPressed = _refreshPressed;
        bool wasClosePressed = _closePressed;
        _refreshPressed = false;
        _closePressed = false;

        if (wasClosePressed && _closeButtonBounds.Contains(e.Location))
        {
            Hide();
            return;
        }

        if (wasRefreshPressed && _refreshButtonBounds.Contains(e.Location))
        {
            TriggerManualRefresh();
            return;
        }

        if (wasRefreshPressed || wasClosePressed)
        {
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_refreshHovered || _closeHovered || _refreshPressed || _closePressed || _activeTooltipCaption is not null)
        {
            _refreshHovered = false;
            _closeHovered = false;
            _refreshPressed = false;
            _closePressed = false;
            _activeTooltipCaption = null;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    private void TriggerManualRefresh()
    {
        if (_refreshPhase == RefreshVisualState.Checking)
        {
            return;
        }

        _refreshPhase = RefreshVisualState.Checking;
        _checkingStartedTick = Environment.TickCount64;
        _manualProbeCompleted = false;
        _bufferedManualSnapshot = null;
        _spinnerAngle = 0f;
        _animTimer.Interval = 30;
        _animTimer.Start();
        Invalidate();

        RefreshRequested?.Invoke();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;

        if (_refreshPhase == RefreshVisualState.Checking)
        {
            _spinnerAngle = (_spinnerAngle + 22f) % 360f;
            long elapsed = now - _checkingStartedTick;

            if ((_manualProbeCompleted && elapsed >= MinCheckingDurationMs) || elapsed >= 2500)
            {
                if (_bufferedManualSnapshot is not null)
                {
                    _snapshot = _bufferedManualSnapshot;
                    _bufferedManualSnapshot = null;
                }

                TransitionToUpdatedConfirmation();
            }

            if (Visible)
            {
                Invalidate();
            }
        }
        else if (_refreshPhase == RefreshVisualState.Updated)
        {
            if (now >= _confirmedUntilTick)
            {
                _refreshPhase = RefreshVisualState.Normal;
                _animTimer.Stop();
                if (Visible)
                {
                    Invalidate();
                }
            }
        }
        else
        {
            _animTimer.Stop();
        }
    }

    private void TransitionToUpdatedConfirmation()
    {
        _refreshPhase = RefreshVisualState.Updated;
        _confirmedUntilTick = Environment.TickCount64 + UpdatedConfirmationDurationMs;
        _animTimer.Interval = 120;
        _animTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            _ageTickTimer.Stop();
            _ageTickTimer.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateScaledSize()
    {
        Size = new Size(ScalePx(LogicalWidth), ScalePx(LogicalHeight));
    }

    private float DpiScale => (IsHandleCreated ? DeviceDpi : 96) / 96f;

    private int ScalePx(float logical) => (int)Math.Round(logical * DpiScale);

    private RefreshVisualState GetEffectiveRefreshVisualState()
    {
        if (_refreshPhase == RefreshVisualState.Checking)
        {
            return RefreshVisualState.Checking;
        }

        if (_refreshPressed && _refreshHovered)
        {
            return RefreshVisualState.Pressed;
        }

        if (_refreshPhase == RefreshVisualState.Updated)
        {
            return RefreshVisualState.Updated;
        }

        return _refreshHovered ? RefreshVisualState.Hover : RefreshVisualState.Normal;
    }

    public static Bitmap RenderCardBitmap(
        HeadsetSnapshot snapshot,
        float scale = 1.0f,
        RefreshVisualState refreshState = RefreshVisualState.Normal,
        bool closeHovered = false,
        bool closePressed = false,
        CardFocusTarget keyboardFocus = CardFocusTarget.None,
        float spinnerAngle = 42f,
        DateTimeOffset? referenceNow = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        float clampedScale = Math.Clamp(scale, 1.0f, 3.0f);
        int width = (int)Math.Round(LogicalWidth * clampedScale);
        int height = (int)Math.Round(LogicalHeight * clampedScale);

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        PaintCardSurface(
            g,
            new Size(width, height),
            clampedScale,
            snapshot,
            refreshState,
            closeHovered,
            closePressed,
            keyboardFocus,
            spinnerAngle,
            referenceNow ?? snapshot.Timestamp,
            out _,
            out _,
            out _);
        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintCardSurface(
            e.Graphics,
            ClientSize,
            DpiScale,
            _snapshot,
            GetEffectiveRefreshVisualState(),
            _closeHovered,
            _closePressed && _closeHovered,
            _keyboardFocus,
            _spinnerAngle,
            DateTimeOffset.Now,
            out _refreshButtonBounds,
            out _closeButtonBounds,
            out _subtitleBounds);
    }

    private static void PaintCardSurface(
        Graphics g,
        Size surfaceSize,
        float s,
        HeadsetSnapshot snapshot,
        RefreshVisualState refreshState,
        bool closeHovered,
        bool closePressed,
        CardFocusTarget keyboardFocus,
        float spinnerAngle,
        DateTimeOffset now,
        out RectangleF refreshButtonBounds,
        out RectangleF closeButtonBounds,
        out RectangleF subtitleBounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        int width = surfaceSize.Width;
        int height = surfaceSize.Height;
        var clientRect = new Rectangle(0, 0, width, height);
        var cardBounds = new RectangleF(0.7f * s, 0.7f * s, width - (1.4f * s), height - (1.4f * s));

        // Windows 11 Light Mode card surface (#FFFFFF -> #F8FAFC) + WCAG 4.76:1 #64748B outer frame
        using (var bgBrush = new LinearGradientBrush(
            clientRect,
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(248, 250, 252),
            LinearGradientMode.Vertical))
        using (var borderPen = new Pen(Color.FromArgb(100, 116, 139), 1.4f * s))
        {
            g.FillRectangle(bgBrush, clientRect);
            g.DrawRectangle(borderPen, cardBounds.X, cardBounds.Y, cardBounds.Width, cardBounds.Height);
        }

        float pad = 18f * s;
        float headerTop = pad;

        // Typography hierarchy: legible at arm's length at 1x (96 DPI) and razor-sharp at 2x
        using var titleFont = new Font("Segoe UI Semibold", 18.0f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var pillFont = new Font("Segoe UI Semibold", 11.6f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var microPillFont = new Font("Segoe UI Semibold", 10.8f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var rowLabelFont = new Font("Segoe UI", 13.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var rowValueFont = new Font("Segoe UI Semibold", 14.0f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var rowHelperFont = new Font("Segoe UI", 13.2f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var footerFont = new Font("Segoe UI", 12.6f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var footerConfirmedFont = new Font("Segoe UI Semibold", 12.6f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var buttonFont = new Font("Segoe UI Semibold", 13.2f * s, FontStyle.Bold, GraphicsUnit.Pixel);

        // 1. Header: Headset icon + optically aligned Title / Status Pill / Close (x) on ~29px axis + plain-English subtitle
        int iconSize = Math.Max(36, (int)Math.Round(46f * s));
        using (Bitmap iconBmp = TrayIconRenderer.RenderBitmap(snapshot.VisualState, iconSize))
        {
            g.DrawImage(iconBmp, pad, headerTop, iconSize, iconSize);
        }

        float closeSize = 28f * s;
        closeButtonBounds = new RectangleF(width - pad - closeSize, headerTop - (2f * s), closeSize, closeSize);
        DrawCloseButton(g, closeButtonBounds, closeHovered, closePressed, keyboardFocus == CardFocusTarget.CloseButton, s);

        (string pillText, Color pillBg, Color pillFg, Color pillBorder) = GetOverallPill(snapshot);
        SizeF pillTextSize = g.MeasureString(pillText, pillFont);
        float pillWidth = pillTextSize.Width + (18f * s);
        float pillHeight = 26f * s;
        var pillRect = new RectangleF(closeButtonBounds.Left - (8f * s) - pillWidth, headerTop - (1f * s), pillWidth, pillHeight);

        using (GraphicsPath pillPath = CreateRoundPath(pillRect, pillHeight / 2f))
        using (var pillBrush = new SolidBrush(pillBg))
        using (var pillBorderPen = new Pen(pillBorder, 1.3f * s))
        using (var pillTextBrush = new SolidBrush(pillFg))
        {
            g.FillPath(pillBrush, pillPath);
            g.DrawPath(pillBorderPen, pillPath);
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(pillText, pillFont, pillTextBrush, pillRect, sfCenter);
        }

        float titleLeft = pad + iconSize + (12f * s);
        float titleMaxWidth = Math.Max(100f, pillRect.Left - titleLeft - (8f * s));
        using (var primaryInkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))  // #0F172A (>17:1)
        using (var subtitleBrush = new SolidBrush(Color.FromArgb(51, 65, 85)))    // #334155 (>10:1)
        {
            var sfTrim = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            var titleRect = new RectangleF(titleLeft, headerTop - (2f * s), titleMaxWidth, 26f * s);
            g.DrawString(snapshot.DeviceDisplayName, titleFont, primaryInkBrush, titleRect, sfTrim);

            // Consumer-friendly plain-English subtitle with >=5px vertical clearance below status pill
            string subtitle = snapshot.DonglePresent
                ? "2.4 GHz Wireless · USB-C Receiver"
                : snapshot.WindowsBluetoothConnected
                    ? "Bluetooth Audio · PC Direct"
                    : "Wireless Gaming Headset";

            float subTop = headerTop + (29f * s);
            subtitleBounds = new RectangleF(titleLeft, subTop, Math.Max(140f, width - pad - titleLeft), 20f * s);
            g.DrawString(subtitle, subFont, subtitleBrush, subtitleBounds, sfTrim);
        }

        // Symmetric 18px vertical spacing below header (18 + 48 + 18 = 84)
        float y = 84f * s;

        // 2. Plain-English Status Rows (#7C8EA6 borders >= 3.05:1 WCAG 1.4.11)
        float rowHeight = 46f * s;
        float rowGap = 8f * s;
        float rowWidth = width - (pad * 2f);

        (string headsetText, Color headsetDot, DotStyle headsetStyle) = FormatHeadsetRow(snapshot);
        string? headsetInlineBadge = snapshot.HeadsetOnline24Ghz && snapshot.MicMuted == true ? "MIC MUTED" : null;
        DrawStatusRow(
            g,
            new RectangleF(pad, y, rowWidth, rowHeight),
            "Headset",
            headsetText,
            headsetDot,
            headsetStyle,
            rowLabelFont,
            rowValueFont,
            s,
            inlineDangerBadge: headsetInlineBadge,
            inlineBadgeFont: microPillFont);
        y += rowHeight + rowGap;

        (string receiverText, Color receiverDot, DotStyle receiverStyle) = FormatReceiverRow(snapshot);
        DrawStatusRow(g, new RectangleF(pad, y, rowWidth, rowHeight), "USB-C Receiver", receiverText, receiverDot, receiverStyle, rowLabelFont, rowValueFont, s);
        y += rowHeight + rowGap;

        (string btText, Color btDot, DotStyle btStyle, bool btSubdued) = FormatBluetoothRow(snapshot);
        DrawStatusRow(
            g,
            new RectangleF(pad, y, rowWidth, rowHeight),
            "Bluetooth",
            btText,
            btDot,
            btStyle,
            rowLabelFont,
            btSubdued ? rowHelperFont : rowValueFont,
            s,
            subduedValue: btSubdued);
        y += rowHeight + rowGap;

        DrawBatteryRow(g, new RectangleF(pad, y, rowWidth, 68f * s), snapshot, rowLabelFont, rowValueFont, rowHelperFont, s);

        // 3. Footer: Symmetric 21px spacing below Battery row (314 -> 335) and above bottom frame (371 -> 392)
        float btnW = 130f * s;
        float btnH = 36f * s;
        float footerTop = 335f * s;
        refreshButtonBounds = new RectangleF(width - pad - btnW, footerTop, btnW, btnH);

        DrawFooterTimestamp(
            g,
            new RectangleF(pad, refreshButtonBounds.Top, refreshButtonBounds.Left - pad - (10f * s), btnH),
            snapshot.Timestamp,
            now,
            refreshState == RefreshVisualState.Updated,
            footerFont,
            footerConfirmedFont);

        DrawRefreshButton(
            g,
            refreshButtonBounds,
            refreshState,
            keyboardFocus == CardFocusTarget.RefreshButton,
            spinnerAngle,
            buttonFont,
            s);
    }

    private static void DrawCloseButton(
        Graphics g,
        RectangleF bounds,
        bool hovered,
        bool pressed,
        bool focused,
        float s)
    {
        float pressShift = pressed ? 1.0f * s : 0f;
        var drawBounds = new RectangleF(bounds.X, bounds.Y + pressShift, bounds.Width, bounds.Height);

        Color fill = pressed
            ? Color.FromArgb(226, 232, 240)  // #E2E8F0
            : hovered
                ? Color.FromArgb(241, 245, 249) // #F1F5F9
                : Color.FromArgb(255, 255, 255); // #FFFFFF
        Color border = pressed || hovered
            ? Color.FromArgb(30, 41, 59)     // #1E293B (14.6:1)
            : Color.FromArgb(71, 85, 105);   // #475569 (7.24:1 interactive affordance)
        Color glyph = pressed || hovered
            ? Color.FromArgb(15, 23, 42)     // #0F172A
            : Color.FromArgb(51, 65, 85);    // #334155

        if (focused)
        {
            DrawFocusRing(g, drawBounds, 7f * s, s);
        }

        using (GraphicsPath path = CreateRoundPath(drawBounds, 7f * s))
        using (var fillBrush = new SolidBrush(fill))
        using (var borderPen = new Pen(border, 1.35f * s))
        {
            g.FillPath(fillBrush, path);
            g.DrawPath(borderPen, path);
        }

        float cx = drawBounds.X + (drawBounds.Width / 2f);
        float cy = drawBounds.Y + (drawBounds.Height / 2f);
        float arm = 4.5f * s;
        using var xPen = new Pen(glyph, 1.85f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(xPen, cx - arm, cy - arm, cx + arm, cy + arm);
        g.DrawLine(xPen, cx + arm, cy - arm, cx - arm, cy + arm);
    }

    private static void DrawRefreshButton(
        Graphics g,
        RectangleF bounds,
        RefreshVisualState state,
        bool focused,
        float spinnerAngle,
        Font font,
        float s)
    {
        bool isPressed = state == RefreshVisualState.Pressed;
        float pressOffset = isPressed ? 1.5f * s : 0f;
        var faceRect = new RectangleF(bounds.X, bounds.Y + pressOffset, bounds.Width, bounds.Height);
        float radius = 8f * s;

        // Subtle tactile bottom lip/elevation shadow when not depressed (suppressed beneath focus halo so ring stays crisp)
        if (!isPressed && !focused)
        {
            var shadowRect = new RectangleF(bounds.X, bounds.Y + (1.8f * s), bounds.Width, bounds.Height);
            using GraphicsPath shadowPath = CreateRoundPath(shadowRect, radius);
            using var shadowBrush = new SolidBrush(Color.FromArgb(28, 15, 23, 42));
            g.FillPath(shadowBrush, shadowPath);
        }

        if (focused)
        {
            DrawFocusRing(g, faceRect, radius, s);
        }

        Color topColor;
        Color bottomColor;
        Color borderColor;
        Color inkColor;
        Color iconColor;
        string label;

        switch (state)
        {
            case RefreshVisualState.Hover:
                topColor = Color.FromArgb(239, 246, 255);    // #EFF6FF
                bottomColor = Color.FromArgb(224, 238, 255);
                borderColor = Color.FromArgb(37, 99, 235);   // #2563EB (4.9:1 border)
                inkColor = Color.FromArgb(30, 64, 175);      // #1E40AF (8.1:1 text)
                iconColor = inkColor;
                label = "Refresh";
                break;

            case RefreshVisualState.Pressed:
                topColor = Color.FromArgb(219, 234, 254);    // #DBEAFE
                bottomColor = Color.FromArgb(209, 228, 254);
                borderColor = Color.FromArgb(29, 78, 216);   // #1D4ED8 (6.2:1 border)
                inkColor = Color.FromArgb(30, 58, 138);      // #1E3A8A (9.5:1 text)
                iconColor = inkColor;
                label = "Refresh";
                break;

            case RefreshVisualState.Checking:
                topColor = Color.FromArgb(239, 246, 255);    // #EFF6FF
                bottomColor = Color.FromArgb(232, 242, 255);
                borderColor = Color.FromArgb(37, 99, 235);   // #2563EB
                inkColor = Color.FromArgb(30, 64, 175);      // #1E40AF (8.1:1 text)
                iconColor = inkColor;
                label = "Checking...";
                break;

            case RefreshVisualState.Updated:
                topColor = Color.FromArgb(220, 252, 231);    // #DCFCE7
                bottomColor = Color.FromArgb(209, 250, 223);
                borderColor = Color.FromArgb(21, 128, 61);   // #15803D (4.8:1 border)
                inkColor = Color.FromArgb(20, 83, 45);       // #14532D (12.7:1 text)
                iconColor = inkColor;
                label = "Updated";
                break;

            default:
                // Strong primary button affordance: #475569 (7.24:1) border + #1D4ED8 (6.12:1) accent icon
                topColor = Color.FromArgb(255, 255, 255);    // #FFFFFF
                bottomColor = Color.FromArgb(241, 245, 249); // #F1F5F9
                borderColor = Color.FromArgb(71, 85, 105);   // #475569 (7.24:1 vs card!)
                inkColor = Color.FromArgb(15, 23, 42);       // #0F172A (16.7:1 text)
                iconColor = Color.FromArgb(29, 78, 216);     // #1D4ED8 (6.12:1 interactive cue)
                label = "Refresh";
                break;
        }

        using (GraphicsPath btnPath = CreateRoundPath(faceRect, radius))
        using (var faceBrush = new LinearGradientBrush(faceRect, topColor, bottomColor, LinearGradientMode.Vertical))
        using (var borderPen = new Pen(borderColor, 1.5f * s))
        {
            g.FillPath(faceBrush, btnPath);
            g.DrawPath(borderPen, btnPath);
        }

        float iconDiameter = 13.5f * s;
        float gap = 6.5f * s;
        SizeF textSize = g.MeasureString(label, font);
        float totalW = iconDiameter + gap + textSize.Width;
        float startX = faceRect.X + ((faceRect.Width - totalW) / 2f) + (2f * s);
        float centerY = faceRect.Y + (faceRect.Height / 2f);

        float iconCx = startX + (iconDiameter / 2f);
        if (state == RefreshVisualState.Updated)
        {
            DrawCheckmarkGlyph(g, iconCx, centerY, iconDiameter * 0.52f, iconColor, 2.1f * s);
        }
        else
        {
            float angle = state == RefreshVisualState.Checking ? spinnerAngle : 0f;
            DrawRefreshArrowGlyph(g, iconCx, centerY, iconDiameter * 0.48f, iconColor, 1.85f * s, angle);
        }

        using var inkBrush = new SolidBrush(inkColor);
        var sfLeftCenter = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var textBounds = new RectangleF(startX + iconDiameter + gap - (2f * s), faceRect.Y, textSize.Width + (8f * s), faceRect.Height);
        g.DrawString(label, font, inkBrush, textBounds, sfLeftCenter);
    }

    private static void DrawFocusRing(Graphics g, RectangleF target, float radius, float s)
    {
        // Windows 11 Fluent high-contrast focus treatment:
        // #FFFFFF 4.4px halo underlay clears any adjacent tint/shadow, crowned by a 2.0px #0F172A (17.85:1) outer ring.
        float pad = 3.5f * s;
        var ringRect = new RectangleF(target.X - pad, target.Y - pad, target.Width + (pad * 2f), target.Height + (pad * 2f));
        using GraphicsPath ringPath = CreateRoundPath(ringRect, radius + pad);
        using var haloPen = new Pen(Color.White, 4.4f * s) { LineJoin = LineJoin.Round };
        using var ringPen = new Pen(Color.FromArgb(15, 23, 42), 2.0f * s) { LineJoin = LineJoin.Round };
        g.DrawPath(haloPen, ringPath);
        g.DrawPath(ringPen, ringPath);
    }

    private static void DrawRefreshArrowGlyph(
        Graphics g,
        float cx,
        float cy,
        float r,
        Color color,
        float strokeWidth,
        float rotationDeg)
    {
        GraphicsState saved = g.Save();
        g.TranslateTransform(cx, cy);
        if (Math.Abs(rotationDeg) > 0.1f)
        {
            g.RotateTransform(rotationDeg);
        }

        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        g.DrawArc(pen, -r, -r, r * 2f, r * 2f, 50f, 285f);

        double tipRad = 335.0 * Math.PI / 180.0;
        float tipX = (float)(Math.Cos(tipRad) * r);
        float tipY = (float)(Math.Sin(tipRad) * r);
        float wing = r * 0.56f;

        g.DrawLines(pen,
        [
            new PointF(tipX - wing, tipY - (wing * 0.15f)),
            new PointF(tipX + (wing * 0.12f), tipY + (wing * 0.12f)),
            new PointF(tipX + (wing * 0.18f), tipY - wing)
        ]);

        g.Restore(saved);
    }

    private static void DrawCheckmarkGlyph(
        Graphics g,
        float cx,
        float cy,
        float r,
        Color color,
        float strokeWidth)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        g.DrawLines(pen,
        [
            new PointF(cx - (r * 0.85f), cy + (r * 0.05f)),
            new PointF(cx - (r * 0.18f), cy + (r * 0.72f)),
            new PointF(cx + (r * 0.95f), cy - (r * 0.65f))
        ]);
    }

    private static void DrawFooterTimestamp(
        Graphics g,
        RectangleF bounds,
        DateTimeOffset sampleTime,
        DateTimeOffset now,
        bool isConfirmedBeat,
        Font normalFont,
        Font confirmedFont)
    {
        string clockText = sampleTime.LocalDateTime.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);

        string caption;
        Font font;
        Color color;

        if (isConfirmedBeat)
        {
            caption = $"Checked just now · {clockText}";
            font = confirmedFont;
            color = Color.FromArgb(20, 83, 45); // #14532D (13.3:1 AAA)
        }
        else
        {
            TimeSpan age = now - sampleTime;
            string relative = age.TotalSeconds < 5
                ? "just now"
                : age.TotalSeconds < 60
                    ? $"{(int)Math.Floor(age.TotalSeconds)}s ago"
                    : age.TotalMinutes < 60
                        ? $"{(int)Math.Floor(age.TotalMinutes)}m ago"
                        : $"{(int)Math.Floor(age.TotalHours)}h ago";

            caption = $"Updated {relative} · {clockText}";
            font = normalFont;
            color = Color.FromArgb(51, 65, 85); // #334155 (9.9:1 AAA)
        }

        using var brush = new SolidBrush(color);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(caption, font, brush, bounds, sf);
    }

    private enum DotStyle
    {
        Solid,
        HollowRing,
        MutedDash
    }

    private static void DrawStatusRow(
        Graphics g,
        RectangleF bounds,
        string label,
        string value,
        Color dotColor,
        DotStyle dotStyle,
        Font labelFont,
        Font valueFont,
        float scale,
        bool subduedValue = false,
        string? inlineDangerBadge = null,
        Font? inlineBadgeFont = null)
    {
        // #7C8EA6 border achieves >= 3.05:1 WCAG 1.4.11 structural contrast on both #FFFFFF and #F1F5F9
        using (GraphicsPath box = CreateRoundPath(bounds, 8f * scale))
        using (var fill = new SolidBrush(Color.FromArgb(241, 245, 249)))         // #F1F5F9
        using (var border = new Pen(Color.FromArgb(124, 142, 166), 1.2f * scale)) // #7C8EA6
        {
            g.FillPath(fill, box);
            g.DrawPath(border, box);
        }

        using var labelBrush = new SolidBrush(Color.FromArgb(51, 65, 85));   // #334155 (9.5:1 AAA)
        using var valueBrush = new SolidBrush(subduedValue
            ? Color.FromArgb(51, 65, 85)     // #334155 (9.5:1 AAA)
            : Color.FromArgb(15, 23, 42));   // #0F172A (16.7:1 AAA)

        var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var sfRight = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        float innerPad = 14f * scale;
        g.DrawString(label, labelFont, labelBrush, new RectangleF(bounds.Left + innerPad, bounds.Top, bounds.Width * 0.42f, bounds.Height), sfLeft);

        float valueLeft = bounds.Left + (bounds.Width * 0.36f);
        if (!string.IsNullOrEmpty(inlineDangerBadge) && inlineBadgeFont is not null)
        {
            SizeF labelSize = g.MeasureString(label, labelFont);
            float badgeW = g.MeasureString(inlineDangerBadge, inlineBadgeFont).Width + (14f * scale);
            float badgeH = 22f * scale;
            var badgeRect = new RectangleF(
                bounds.Left + innerPad + labelSize.Width + (6f * scale),
                bounds.Top + ((bounds.Height - badgeH) / 2f),
                badgeW,
                badgeH);

            using (GraphicsPath badgePath = CreateRoundPath(badgeRect, badgeH / 2f))
            using (var badgeBg = new SolidBrush(Color.FromArgb(254, 226, 226)))           // #FEE2E2
            using (var badgeBorder = new Pen(Color.FromArgb(220, 38, 38), 1.15f * scale)) // #DC2626
            using (var badgeFg = new SolidBrush(Color.FromArgb(127, 29, 29)))             // #7F1D1D (9.7:1 AAA)
            {
                g.FillPath(badgeBg, badgePath);
                g.DrawPath(badgeBorder, badgePath);
                var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(inlineDangerBadge, inlineBadgeFont, badgeFg, badgeRect, sfCenter);
            }

            valueLeft = Math.Max(valueLeft, badgeRect.Right + (8f * scale));
        }

        float dotR = dotStyle == DotStyle.MutedDash ? 5.5f * scale : 5.0f * scale;
        float dotCx = bounds.Right - innerPad - (5.5f * scale);
        float dotCy = bounds.Top + (bounds.Height / 2f);

        switch (dotStyle)
        {
            case DotStyle.HollowRing:
            {
                using var centerFill = new SolidBrush(Color.White);
                using var ringPen = new Pen(dotColor, 2.3f * scale);
                g.FillEllipse(centerFill, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
                g.DrawEllipse(ringPen, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
                break;
            }

            case DotStyle.MutedDash:
            {
                // Crisp 11px circle on #FFFFFF with #475569 (6.92:1) ring and well-separated inner horizontal dash
                using var centerFill = new SolidBrush(Color.White);
                using var ringPen = new Pen(dotColor, 1.5f * scale);
                using var dashPen = new Pen(dotColor, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.FillEllipse(centerFill, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
                g.DrawEllipse(ringPen, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
                float halfDash = dotR * 0.36f;
                g.DrawLine(dashPen, dotCx - halfDash, dotCy, dotCx + halfDash, dotCy);
                break;
            }

            default:
            {
                using var haloBrush = new SolidBrush(Color.FromArgb(38, dotColor.R, dotColor.G, dotColor.B));
                using var dotBrush = new SolidBrush(dotColor);
                float haloR = dotR + (2.2f * scale);
                g.FillEllipse(haloBrush, dotCx - haloR, dotCy - haloR, haloR * 2f, haloR * 2f);
                g.FillEllipse(dotBrush, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
                break;
            }
        }

        float valueRight = dotCx - (5.5f * scale) - (10f * scale);
        g.DrawString(value, valueFont, valueBrush, new RectangleF(valueLeft, bounds.Top, Math.Max(40f, valueRight - valueLeft), bounds.Height), sfRight);
    }

    private static void DrawBatteryRow(
        Graphics g,
        RectangleF bounds,
        HeadsetSnapshot snapshot,
        Font labelFont,
        Font valueFont,
        Font helperFont,
        float scale)
    {
        using (GraphicsPath box = CreateRoundPath(bounds, 8f * scale))
        using (var fill = new SolidBrush(Color.FromArgb(241, 245, 249)))          // #F1F5F9
        using (var border = new Pen(Color.FromArgb(124, 142, 166), 1.2f * scale)) // #7C8EA6 (>= 3.05:1)
        {
            g.FillPath(fill, box);
            g.DrawPath(border, box);
        }

        float innerPad = 14f * scale;
        using var labelBrush = new SolidBrush(Color.FromArgb(51, 65, 85));        // #334155 (9.5:1 AAA)
        using var primaryValueBrush = new SolidBrush(Color.FromArgb(15, 23, 42)); // #0F172A (16.7:1 AAA)
        using var helperValueBrush = new SolidBrush(Color.FromArgb(51, 65, 85));  // #334155 (9.5:1 AAA)

        var topLineRect = new RectangleF(bounds.Left + innerPad, bounds.Top + (8f * scale), bounds.Width - (innerPad * 2f), 24f * scale);
        var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var sfRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        g.DrawString("Battery", labelFont, labelBrush, topLineRect, sfLeft);

        string battText;
        Color barColor;
        float ratio;
        bool hasLiveGauge;

        if (snapshot.BatteryPercent is int pct)
        {
            int clamped = Math.Clamp(pct, 0, 100);
            ratio = clamped / 100f;
            hasLiveGauge = true;
            battText = snapshot.Charging == BatteryChargingState.Charging
                ? $"{clamped}% · Charging"
                : $"{clamped}%";

            barColor = snapshot.Charging == BatteryChargingState.Charging
                ? Color.FromArgb(29, 78, 216)    // #1D4ED8 (5.2:1 vs track)
                : clamped >= 35
                    ? Color.FromArgb(21, 128, 61)  // #15803D (4.0:1 vs track)
                    : clamped >= 15
                        ? Color.FromArgb(180, 83, 9)  // #B45309 (4.2:1 vs track)
                        : Color.FromArgb(220, 38, 38); // #DC2626 (3.9:1 vs track)

            g.DrawString(battText, valueFont, primaryValueBrush, topLineRect, sfRight);
        }
        else if (snapshot.Charging == BatteryChargingState.Charging)
        {
            ratio = 0.5f;
            hasLiveGauge = true;
            battText = "Charging";
            barColor = Color.FromArgb(29, 78, 216);
            g.DrawString(battText, valueFont, primaryValueBrush, topLineRect, sfRight);
        }
        else
        {
            ratio = 0f;
            hasLiveGauge = false;
            barColor = Color.FromArgb(100, 116, 139);
            battText = snapshot.PcConnection switch
            {
                PcConnectionState.DongleOnlyHeadsetOff => "Turn headset on to read",
                PcConnectionState.ConnectedBluetooth => "Not reported over Bluetooth",
                _ => "Connect headset to read"
            };
            g.DrawString(battText, helperFont, helperValueBrush, topLineRect, sfRight);
        }

        float barH = 10f * scale;
        var trackRect = new RectangleF(
            bounds.Left + innerPad,
            bounds.Bottom - innerPad - barH + (1f * scale),
            bounds.Width - (innerPad * 2f),
            barH);

        if (hasLiveGauge)
        {
            using (GraphicsPath trackPath = CreateRoundPath(trackRect, barH / 2f))
            using (var trackBrush = new SolidBrush(Color.FromArgb(226, 232, 240))) // #E2E8F0
            using (var trackBorder = new Pen(Color.FromArgb(100, 116, 139), 1.15f * scale)) // #64748B (4.34:1 vs #F1F5F9)
            {
                g.FillPath(trackBrush, trackPath);

                if (ratio > 0.01f)
                {
                    float fillW = Math.Max(barH, trackRect.Width * ratio);
                    var fillRect = new RectangleF(trackRect.X, trackRect.Y, fillW, barH);
                    using GraphicsPath fillPath = CreateRoundPath(fillRect, barH / 2f);
                    using var barBrush = new SolidBrush(barColor);
                    g.FillPath(barBrush, fillPath);
                }

                g.DrawPath(trackBorder, trackPath);
            }
        }
        else
        {
            // 4-segment standby track with #64748B (4.34:1) borders clearly distinguishes asleep from 0% empty
            const int segments = 4;
            float segGap = 6f * scale;
            float segW = (trackRect.Width - (segGap * (segments - 1))) / segments;
            using var segFill = new SolidBrush(Color.FromArgb(226, 232, 240));
            using var segBorder = new Pen(Color.FromArgb(100, 116, 139), 1.15f * scale);
            for (int i = 0; i < segments; i++)
            {
                var segRect = new RectangleF(trackRect.X + (i * (segW + segGap)), trackRect.Y, segW, barH);
                using GraphicsPath segPath = CreateRoundPath(segRect, barH / 2f);
                g.FillPath(segFill, segPath);
                g.DrawPath(segBorder, segPath);
            }
        }
    }

    private static (string Text, Color Bg, Color Fg, Color Border) GetOverallPill(HeadsetSnapshot s) => s.PcConnection switch
    {
        PcConnectionState.Connected24Ghz => (
            "PC CONNECTED",
            Color.FromArgb(220, 252, 231), // #DCFCE7
            Color.FromArgb(20, 83, 45),    // #14532D (12.7:1 AAA)
            Color.FromArgb(21, 128, 61)),  // #15803D (5.0:1 border)
        PcConnectionState.ConnectedBluetooth => (
            "PC BLUETOOTH",
            Color.FromArgb(219, 234, 254), // #DBEAFE
            Color.FromArgb(30, 58, 138),   // #1E3A8A (9.5:1 AAA)
            Color.FromArgb(29, 78, 216)),  // #1D4ED8 (6.5:1 border)
        PcConnectionState.DongleOnlyHeadsetOff => (
            "HEADSET OFF",
            Color.FromArgb(254, 243, 199), // #FEF3C7
            Color.FromArgb(120, 53, 15),   // #78350F (9.6:1 AAA)
            Color.FromArgb(180, 83, 9)),   // #B45309 (5.3:1 border)
        _ => (
            "DISCONNECTED",
            Color.FromArgb(254, 226, 226), // #FEE2E2
            Color.FromArgb(127, 29, 29),   // #7F1D1D (9.7:1 AAA)
            Color.FromArgb(220, 38, 38))   // #DC2626 (4.8:1 border)
    };

    private static (string Text, Color Dot, DotStyle Style) FormatHeadsetRow(HeadsetSnapshot s) => s.PcConnection switch
    {
        PcConnectionState.Connected24Ghz => ("Connected (2.4 GHz)", Color.FromArgb(21, 128, 61), DotStyle.Solid),
        PcConnectionState.ConnectedBluetooth => ("Connected (Bluetooth)", Color.FromArgb(29, 78, 216), DotStyle.Solid),
        PcConnectionState.DongleOnlyHeadsetOff => ("Off or Asleep", Color.FromArgb(180, 83, 9), DotStyle.HollowRing),
        _ => ("Disconnected", Color.FromArgb(220, 38, 38), DotStyle.Solid)
    };

    private static (string Text, Color Dot, DotStyle Style) FormatReceiverRow(HeadsetSnapshot s)
    {
        if (!s.DonglePresent)
        {
            return ("Unplugged", Color.FromArgb(71, 85, 105), DotStyle.MutedDash);
        }

        return s.HeadsetOnline24Ghz
            ? ("Plugged In · Active", Color.FromArgb(21, 128, 61), DotStyle.Solid)
            : ("Plugged In · Waiting", Color.FromArgb(180, 83, 9), DotStyle.HollowRing);
    }

    private static (string Text, Color Dot, DotStyle Style, bool Subdued) FormatBluetoothRow(HeadsetSnapshot s)
    {
        if (s.PcConnection == PcConnectionState.DongleOnlyHeadsetOff && !s.WindowsBluetoothConnected)
        {
            return ("Unavailable while off", Color.FromArgb(71, 85, 105), DotStyle.MutedDash, true);
        }

        return s.Bluetooth switch
        {
            BluetoothState.Connected when s.WindowsBluetoothConnected => ("Connected to PC", Color.FromArgb(29, 78, 216), DotStyle.Solid, false),
            BluetoothState.Connected => ("Connected", Color.FromArgb(29, 78, 216), DotStyle.Solid, false),
            BluetoothState.Standby => ("On · Waiting", Color.FromArgb(29, 78, 216), DotStyle.HollowRing, false),
            _ => ("Off", Color.FromArgb(71, 85, 105), DotStyle.MutedDash, false)
        };
    }

    private static GraphicsPath CreateRoundPath(RectangleF rect, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
