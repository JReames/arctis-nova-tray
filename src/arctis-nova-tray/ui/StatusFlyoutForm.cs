using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;

namespace ArctisNovaTray.Ui;

public sealed partial class StatusFlyoutForm : Form
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private HeadsetSnapshot _snapshot = HeadsetSnapshot.CreateInitial();
    private RectangleF _refreshButtonBounds;
    private bool _refreshHovered;
    private bool _isRefreshing;
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
        UpdateScaledSize();
    }

    public bool WasRecentlyDeactivated =>
        Environment.TickCount64 - Interlocked.Read(ref _lastDeactivatedTick) < 220;

    public void UpdateSnapshot(HeadsetSnapshot snapshot, bool clearRefreshing = true)
    {
        _snapshot = snapshot;
        if (clearRefreshing)
        {
            _isRefreshing = false;
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
        if (!Visible)
        {
            Show();
        }

        _ = SetForegroundWindow(Handle);
        BringToFront();
        Activate();
        Invalidate();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
        }
        else if (e.KeyCode == Keys.F5)
        {
            TriggerRefresh();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool overRefresh = _refreshButtonBounds.Contains(e.Location);
        if (overRefresh != _refreshHovered)
        {
            _refreshHovered = overRefresh;
            Cursor = overRefresh ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_refreshHovered)
        {
            _refreshHovered = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _refreshButtonBounds.Contains(e.Location))
        {
            TriggerRefresh();
        }
    }

    private void TriggerRefresh()
    {
        _isRefreshing = true;
        Invalidate();
        RefreshRequested?.Invoke();
    }

    private void UpdateScaledSize()
    {
        Size = new Size(ScalePx(356), ScalePx(312));
    }

    private float DpiScale => (IsHandleCreated ? DeviceDpi : 96) / 96f;

    private int ScalePx(float logical) => (int)Math.Round(logical * DpiScale);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float s = DpiScale;
        var cardBounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        // Crisp Windows 11 Light Mode card surface (#FFFFFF -> #F8FAFC) + #CBD5E1 border
        using (var bgBrush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(248, 250, 252),
            LinearGradientMode.Vertical))
        using (var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1.2f * s))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
            g.DrawRectangle(borderPen, cardBounds.X, cardBounds.Y, cardBounds.Width, cardBounds.Height);
        }

        float pad = 16f * s;
        float y = pad;

        // Header: dynamic rendered headset icon + title + status pill
        int iconSize = Math.Max(32, ScalePx(42));
        using (Bitmap iconBmp = TrayIconRenderer.RenderBitmap(_snapshot.VisualState, iconSize))
        {
            g.DrawImage(iconBmp, pad, y, iconSize, iconSize);
        }

        using var titleFont = new Font("Segoe UI Semibold", 11.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", 8.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var badgeFont = new Font("Segoe UI Semibold", 8.0f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var rowLabelFont = new Font("Segoe UI", 9.0f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var rowValueFont = new Font("Segoe UI Semibold", 9.2f * s, FontStyle.Bold, GraphicsUnit.Pixel);

        (string pillText, Color pillBg, Color pillFg, Color pillBorder) = GetOverallPill(_snapshot);
        SizeF pillTextSize = g.MeasureString(pillText, badgeFont);
        float pillWidth = pillTextSize.Width + (14f * s);
        float pillHeight = 22f * s;
        var pillRect = new RectangleF(Width - pad - pillWidth, y + (4f * s), pillWidth, pillHeight);

        using (GraphicsPath pillPath = CreateRoundPath(pillRect, pillHeight / 2f))
        using (var pillBrush = new SolidBrush(pillBg))
        using (var pillBorderPen = new Pen(pillBorder, 1f * s))
        using (var pillTextBrush = new SolidBrush(pillFg))
        {
            g.FillPath(pillBrush, pillPath);
            g.DrawPath(pillBorderPen, pillPath);
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(pillText, badgeFont, pillTextBrush, pillRect, sfCenter);
        }

        float titleLeft = pad + iconSize + (11f * s);
        float titleMaxWidth = Math.Max(80f, pillRect.Left - titleLeft - (8f * s));
        using (var primaryInkBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))   // #0F172A
        using (var subtitleBrush = new SolidBrush(Color.FromArgb(71, 85, 105)))    // #475569
        {
            var titleRect = new RectangleF(titleLeft, y + (1f * s), titleMaxWidth, 22f * s);
            var sfTrim = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(_snapshot.DeviceDisplayName, titleFont, primaryInkBrush, titleRect, sfTrim);

            string subtitle = _snapshot.DonglePresent && !string.IsNullOrWhiteSpace(_snapshot.DongleVidPid)
                ? $"SteelSeries Wireless · USB {_snapshot.DongleVidPid}"
                : _snapshot.WindowsBluetoothConnected
                    ? "SteelSeries Wireless · PC Bluetooth"
                    : "SteelSeries Arctis Nova 7 Status";
            g.DrawString(subtitle, subFont, subtitleBrush, new RectangleF(titleLeft, y + (22f * s), titleMaxWidth + pillWidth, 18f * s), sfTrim);
        }

        y += iconSize + (14f * s);

        // Status rows
        float rowHeight = 40f * s;
        float rowGap = 7f * s;
        float rowWidth = Width - (pad * 2f);

        (string pcText, Color pcDot) = FormatPcRow(_snapshot);
        DrawStatusRow(g, new RectangleF(pad, y, rowWidth, rowHeight), "PC 2.4GHz / Link", pcText, pcDot, rowLabelFont, rowValueFont, s);
        y += rowHeight + rowGap;

        (string dongleText, Color dongleDot) = FormatDongleRow(_snapshot);
        DrawStatusRow(g, new RectangleF(pad, y, rowWidth, rowHeight), "USB Dongle", dongleText, dongleDot, rowLabelFont, rowValueFont, s);
        y += rowHeight + rowGap;

        (string btText, Color btDot, bool btHollow) = FormatBluetoothRow(_snapshot);
        DrawStatusRow(g, new RectangleF(pad, y, rowWidth, rowHeight), "Bluetooth", btText, btDot, rowLabelFont, rowValueFont, s, btHollow);
        y += rowHeight + rowGap;

        DrawBatteryRow(g, new RectangleF(pad, y, rowWidth, rowHeight + (4f * s)), _snapshot, rowLabelFont, rowValueFont, s);

        // Footer: timestamp + Refresh button
        float btnW = 92f * s;
        float btnH = 28f * s;
        _refreshButtonBounds = new RectangleF(Width - pad - btnW, Height - pad - btnH + (2f * s), btnW, btnH);

        using (var timestampBrush = new SolidBrush(Color.FromArgb(100, 116, 139))) // #64748B
        {
            string updated = $"Updated {_snapshot.Timestamp.LocalDateTime:HH:mm:ss}";
            g.DrawString(updated, subFont, timestampBrush, pad, _refreshButtonBounds.Top + (6f * s));
        }

        Color btnFill = _isRefreshing
            ? Color.FromArgb(239, 246, 255) // #EFF6FF
            : _refreshHovered
                ? Color.FromArgb(241, 245, 249) // #F1F5F9
                : Color.FromArgb(255, 255, 255); // #FFFFFF
        Color btnBorder = _isRefreshing || _refreshHovered
            ? Color.FromArgb(59, 130, 246) // #3B82F6
            : Color.FromArgb(203, 213, 225); // #CBD5E1
        Color btnText = _isRefreshing
            ? Color.FromArgb(29, 78, 216) // #1D4ED8
            : Color.FromArgb(15, 23, 42); // #0F172A

        using (GraphicsPath btnPath = CreateRoundPath(_refreshButtonBounds, 6f * s))
        using (var btnBrush = new SolidBrush(btnFill))
        using (var btnPen = new Pen(btnBorder, 1f * s))
        using (var btnTextBrush = new SolidBrush(btnText))
        {
            g.FillPath(btnBrush, btnPath);
            g.DrawPath(btnPen, btnPath);
            string btnLabel = _isRefreshing ? "Refreshing..." : "Refresh";
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(btnLabel, badgeFont, btnTextBrush, _refreshButtonBounds, sfCenter);
        }
    }

    private static void DrawStatusRow(
        Graphics g,
        RectangleF bounds,
        string label,
        string value,
        Color dotColor,
        Font labelFont,
        Font valueFont,
        float scale,
        bool hollowDot = false)
    {
        using (GraphicsPath box = CreateRoundPath(bounds, 7f * scale))
        using (var fill = new SolidBrush(Color.FromArgb(241, 245, 249)))     // #F1F5F9
        using (var border = new Pen(Color.FromArgb(226, 232, 240), 1f * scale)) // #E2E8F0
        {
            g.FillPath(fill, box);
            g.DrawPath(border, box);
        }

        using var labelBrush = new SolidBrush(Color.FromArgb(71, 85, 105));  // #475569
        using var valueBrush = new SolidBrush(Color.FromArgb(15, 23, 42));   // #0F172A

        var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var sfRight = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        float innerPad = 12f * scale;
        g.DrawString(label, labelFont, labelBrush, new RectangleF(bounds.Left + innerPad, bounds.Top, bounds.Width * 0.42f, bounds.Height), sfLeft);

        float dotR = 4.3f * scale;
        float dotCx = bounds.Right - innerPad - dotR;
        float dotCy = bounds.Top + (bounds.Height / 2f);

        if (hollowDot)
        {
            using var hollowFill = new SolidBrush(Color.FromArgb(239, 246, 255)); // #EFF6FF
            using var ringPen = new Pen(dotColor, 2.0f * scale);
            g.FillEllipse(hollowFill, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
            g.DrawEllipse(ringPen, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
        }
        else
        {
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, dotCx - dotR, dotCy - dotR, dotR * 2f, dotR * 2f);
        }

        float valueRight = dotCx - dotR - (8f * scale);
        float valueLeft = bounds.Left + (bounds.Width * 0.38f);
        g.DrawString(value, valueFont, valueBrush, new RectangleF(valueLeft, bounds.Top, Math.Max(40f, valueRight - valueLeft), bounds.Height), sfRight);
    }

    private static void DrawBatteryRow(
        Graphics g,
        RectangleF bounds,
        HeadsetSnapshot snapshot,
        Font labelFont,
        Font valueFont,
        float scale)
    {
        using (GraphicsPath box = CreateRoundPath(bounds, 7f * scale))
        using (var fill = new SolidBrush(Color.FromArgb(241, 245, 249)))     // #F1F5F9
        using (var border = new Pen(Color.FromArgb(226, 232, 240), 1f * scale)) // #E2E8F0
        {
            g.FillPath(fill, box);
            g.DrawPath(border, box);
        }

        float innerPad = 12f * scale;
        using var labelBrush = new SolidBrush(Color.FromArgb(71, 85, 105));  // #475569
        using var valueBrush = new SolidBrush(Color.FromArgb(15, 23, 42));   // #0F172A

        float topTextY = bounds.Top + (6f * scale);
        g.DrawString("Battery", labelFont, labelBrush, bounds.Left + innerPad, topTextY);

        string battText;
        Color barColor;
        float ratio;

        if (snapshot.BatteryPercent is int pct)
        {
            int clamped = Math.Clamp(pct, 0, 100);
            ratio = clamped / 100f;
            battText = snapshot.Charging == BatteryChargingState.Charging
                ? $"{clamped}% · Charging"
                : $"{clamped}%";
            barColor = snapshot.Charging == BatteryChargingState.Charging
                ? Color.FromArgb(2, 132, 199)    // #0284C7
                : clamped >= 35
                    ? Color.FromArgb(22, 163, 74)  // #16A34A
                    : clamped >= 15
                        ? Color.FromArgb(217, 119, 6) // #D97706
                        : Color.FromArgb(220, 38, 38); // #DC2626
        }
        else if (snapshot.Charging == BatteryChargingState.Charging)
        {
            ratio = 0.5f;
            battText = "Charging";
            barColor = Color.FromArgb(2, 132, 199);
        }
        else
        {
            ratio = 0f;
            battText = "Offline / N/A";
            barColor = Color.FromArgb(148, 163, 184);
        }

        var sfRight = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(battText, valueFont, valueBrush, new RectangleF(bounds.Left + innerPad, topTextY, bounds.Width - (innerPad * 2f), 20f * scale), sfRight);

        // Progress track (#E2E8F0)
        float barH = 6f * scale;
        var trackRect = new RectangleF(bounds.Left + innerPad, bounds.Bottom - innerPad - barH + (2f * scale), bounds.Width - (innerPad * 2f), barH);
        using (GraphicsPath trackPath = CreateRoundPath(trackRect, barH / 2f))
        using (var trackBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
        {
            g.FillPath(trackBrush, trackPath);
        }

        if (ratio > 0.01f)
        {
            float fillW = Math.Max(barH, trackRect.Width * ratio);
            var fillRect = new RectangleF(trackRect.X, trackRect.Y, fillW, barH);
            using GraphicsPath fillPath = CreateRoundPath(fillRect, barH / 2f);
            using var barBrush = new SolidBrush(barColor);
            g.FillPath(barBrush, fillPath);
        }
    }

    private static (string Text, Color Bg, Color Fg, Color Border) GetOverallPill(HeadsetSnapshot s) => s.PcConnection switch
    {
        PcConnectionState.Connected24Ghz => (
            "PC CONNECTED",
            Color.FromArgb(220, 252, 231), // #DCFCE7
            Color.FromArgb(22, 101, 52),   // #166534
            Color.FromArgb(134, 239, 172)), // #86EFAC
        PcConnectionState.ConnectedBluetooth => (
            "PC BLUETOOTH",
            Color.FromArgb(219, 234, 254), // #DBEAFE
            Color.FromArgb(30, 64, 175),   // #1E40AF
            Color.FromArgb(147, 197, 253)), // #93C5FD
        PcConnectionState.DongleOnlyHeadsetOff => (
            "HEADSET OFF",
            Color.FromArgb(254, 243, 199), // #FEF3C7
            Color.FromArgb(146, 64, 14),   // #92400E
            Color.FromArgb(252, 211, 77)), // #FCD34D
        _ => (
            "DISCONNECTED",
            Color.FromArgb(254, 226, 226), // #FEE2E2
            Color.FromArgb(153, 27, 27),   // #991B1B
            Color.FromArgb(252, 165, 165)) // #FCA5A5
    };

    private static (string Text, Color Dot) FormatPcRow(HeadsetSnapshot s) => s.PcConnection switch
    {
        PcConnectionState.Connected24Ghz => ("Connected (2.4GHz)", Color.FromArgb(22, 163, 74)),
        PcConnectionState.ConnectedBluetooth => ("Connected (PC BT)", Color.FromArgb(37, 99, 235)),
        PcConnectionState.DongleOnlyHeadsetOff => ("Headset Off / Sleeping", Color.FromArgb(217, 119, 6)),
        _ => ("Disconnected", Color.FromArgb(220, 38, 38))
    };

    private static (string Text, Color Dot) FormatDongleRow(HeadsetSnapshot s)
    {
        if (!s.DonglePresent)
        {
            return ("Unplugged", Color.FromArgb(100, 116, 139));
        }

        return s.HeadsetOnline24Ghz
            ? ("Plugged In · Paired", Color.FromArgb(22, 163, 74))
            : ("Plugged In · Standby", Color.FromArgb(217, 119, 6));
    }

    private static (string Text, Color Dot, bool Hollow) FormatBluetoothRow(HeadsetSnapshot s) => s.Bluetooth switch
    {
        BluetoothState.Connected when s.WindowsBluetoothConnected => ("ON · Connected (PC)", Color.FromArgb(37, 99, 235), false),
        BluetoothState.Connected => ("ON · Connected", Color.FromArgb(37, 99, 235), false),
        BluetoothState.Standby => ("ON · Standby", Color.FromArgb(37, 99, 235), true),
        _ => ("OFF", Color.FromArgb(100, 116, 139), false)
    };

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
