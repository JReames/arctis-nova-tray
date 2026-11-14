using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;
using ArctisNovaTray.Ui;

namespace ArctisNovaTray.Tests;

internal static class DocAssetGenerator
{
    public static IReadOnlyList<string> GenerateUxPreviews(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var written = new List<string>();

        HeadsetSnapshot connectedSnap = CreateShowcaseSnapshot();
        DateTimeOffset refNowImmediate = connectedSnap.Timestamp.AddSeconds(2);
        DateTimeOffset refNow14Sec = connectedSnap.Timestamp.AddSeconds(14);

        var headsetOffSnap = new HeadsetSnapshot
        {
            Timestamp = connectedSnap.Timestamp,
            PcConnection = PcConnectionState.DongleOnlyHeadsetOff,
            Bluetooth = BluetoothState.Off,
            BluetoothDetectionSource = BluetoothSource.None,
            DonglePresent = true,
            DongleProductName = "Arctis Nova 7",
            DongleVidPid = "1038:22A1",
            HeadsetOnline24Ghz = false,
            BatteryPercent = null,
            Charging = BatteryChargingState.Disconnected
        };

        var micMutedChargingSnap = new HeadsetSnapshot
        {
            Timestamp = connectedSnap.Timestamp,
            PcConnection = PcConnectionState.Connected24Ghz,
            Bluetooth = BluetoothState.Standby,
            BluetoothDetectionSource = BluetoothSource.DongleHidReport,
            DonglePresent = true,
            DongleProductName = "Arctis Nova 7X Gen 2",
            DongleVidPid = "1038:22A5",
            HeadsetOnline24Ghz = true,
            BatteryPercent = 42,
            Charging = BatteryChargingState.Charging,
            MicMuted = true
        };

        var disconnectedSnap = new HeadsetSnapshot
        {
            Timestamp = connectedSnap.Timestamp,
            PcConnection = PcConnectionState.Disconnected,
            Bluetooth = BluetoothState.Off,
            BluetoothDetectionSource = BluetoothSource.None,
            DonglePresent = false,
            HeadsetOnline24Ghz = false,
            BatteryPercent = null,
            Charging = BatteryChargingState.Disconnected
        };

        (string Slug, HeadsetSnapshot Snap, RefreshVisualState Refresh, bool CloseHover, CardFocusTarget Focus, DateTimeOffset RefNow)[] cardCases =
        [
            ("card-connected", connectedSnap, RefreshVisualState.Normal, false, CardFocusTarget.None, refNowImmediate),
            ("card-headset-off", headsetOffSnap, RefreshVisualState.Normal, false, CardFocusTarget.None, refNow14Sec),
            ("card-mic-muted-charging", micMutedChargingSnap, RefreshVisualState.Normal, false, CardFocusTarget.None, refNowImmediate),
            ("card-disconnected-close-focus", disconnectedSnap, RefreshVisualState.Normal, true, CardFocusTarget.CloseButton, refNow14Sec),
            ("card-refresh-hover", connectedSnap, RefreshVisualState.Hover, false, CardFocusTarget.RefreshButton, refNowImmediate),
            ("card-refresh-pressed", connectedSnap, RefreshVisualState.Pressed, false, CardFocusTarget.None, refNowImmediate),
            ("card-refresh-checking", connectedSnap, RefreshVisualState.Checking, false, CardFocusTarget.None, refNowImmediate),
            ("card-refresh-updated", connectedSnap, RefreshVisualState.Updated, true, CardFocusTarget.None, refNowImmediate)
        ];

        foreach (var c in cardCases)
        {
            foreach ((string suffix, float scale) in new[] { ("1x", 1.0f), ("2x", 2.0f) })
            {
                string path = Path.Combine(outputDir, $"{c.Slug}-{suffix}.png");
                using Bitmap bmp = StatusFlyoutForm.RenderCardBitmap(
                    c.Snap,
                    scale: scale,
                    refreshState: c.Refresh,
                    closeHovered: c.CloseHover,
                    closePressed: false,
                    keyboardFocus: c.Focus,
                    spinnerAngle: 52f,
                    referenceNow: c.RefNow);
                bmp.Save(path, ImageFormat.Png);
                written.Add(path);
            }
        }

        // Right-click ContextMenuStrip preview at 1x and 2x (including Charging summary header)
        foreach ((string suffix, float scale) in new[] { ("1x", 1.0f), ("2x", 2.0f) })
        {
            string menuPath = Path.Combine(outputDir, $"context-menu-{suffix}.png");
            using Bitmap menuBmp = RenderContextMenuPreview(micMutedChargingSnap, scale);
            menuBmp.Save(menuPath, ImageFormat.Png);
            written.Add(menuPath);
        }

        // Native 1x (exact 16x16 & 32x32 pixels) + 2x inspection sheet on #18181B Dark & #F3F4F6 Light Windows 11 taskbars
        string strip1xPath = Path.Combine(outputDir, "taskbar-icons-dark-and-light-1x.png");
        using (Bitmap strip1x = RenderTaskbarComparisonSheet(scaleFactor: 1))
        {
            strip1x.Save(strip1xPath, ImageFormat.Png);
            written.Add(strip1xPath);
        }

        string strip2xPath = Path.Combine(outputDir, "taskbar-icons-dark-and-light-2x.png");
        using (Bitmap strip2x = RenderTaskbarComparisonSheet(scaleFactor: 2))
        {
            strip2x.Save(strip2xPath, ImageFormat.Png);
            written.Add(strip2xPath);
        }

        return written;
    }

    private static Bitmap RenderContextMenuPreview(HeadsetSnapshot snapshot, float scale)
    {
        float s = Math.Clamp(scale, 1.0f, 3.0f);
        string summary = TooltipFormatter.FormatMenuSummary(snapshot);

        int canvasW = (int)Math.Round(380f * s);
        int canvasH = (int)Math.Round(210f * s);
        var bmp = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);

        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(241, 245, 249));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var regFont = new Font("Segoe UI", 13.33f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var boldFont = new Font("Segoe UI Semibold", 13.33f * s, FontStyle.Bold, GraphicsUnit.Pixel);

        float summaryW = g.MeasureString(summary, regFont).Width;
        float menuW = Math.Max(260f * s, summaryW + (36f * s));
        float itemH = 33f * s;
        float sepH = 9f * s;
        float padX = 5f * s;
        float padY = 6f * s;
        float menuH = (padY * 2f) + (itemH * 4f) + (sepH * 2f);

        var menuRect = new RectangleF((canvasW - menuW) / 2f, (canvasH - menuH) / 2f, menuW, menuH);
        DrawSoftShadow(g, menuRect, 6f * s, 8, 18);

        using (var surfaceBrush = new SolidBrush(TrayApplicationContext.MenuSurfaceColor))
        using (var outerPen = new Pen(TrayApplicationContext.MenuOuterBorderColor, 1.2f * s))
        {
            g.FillRectangle(surfaceBrush, menuRect);
            g.DrawRectangle(outerPen, menuRect.X, menuRect.Y, menuRect.Width, menuRect.Height);
        }

        using var headerBrush = new SolidBrush(TrayApplicationContext.MenuHeaderInkColor);
        using var primaryBrush = new SolidBrush(TrayApplicationContext.MenuPrimaryInkColor);
        using var hoverFill = new SolidBrush(TrayApplicationContext.MenuHoverFillColor);
        using var hoverPen = new Pen(TrayApplicationContext.MenuHoverBorderColor, 1.1f * s);
        using var sepPen = new Pen(TrayApplicationContext.MenuSeparatorColor, 1.0f * s);
        var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        float y = menuRect.Y + padY;
        float textX = menuRect.X + (16f * s);
        float textW = menuRect.Width - (32f * s);

        // 1. Disabled high-contrast summary header (#334155 10.3:1)
        g.DrawString(summary, regFont, headerBrush, new RectangleF(textX, y, textW, itemH), sfLeft);
        y += itemH;

        // Separator 1
        float sepY1 = y + (sepH / 2f);
        g.DrawLine(sepPen, menuRect.X + (12f * s), sepY1, menuRect.Right - (12f * s), sepY1);
        y += sepH;

        // 2. Open Status Card (hovered state)
        var hoverRect = new RectangleF(menuRect.X + padX, y + (1f * s), menuRect.Width - (padX * 2f), itemH - (2f * s));
        g.FillRectangle(hoverFill, hoverRect);
        g.DrawRectangle(hoverPen, hoverRect.X, hoverRect.Y, hoverRect.Width, hoverRect.Height);
        g.DrawString("Open Status Card", boldFont, primaryBrush, new RectangleF(textX, y, textW, itemH), sfLeft);
        y += itemH;

        // 3. Refresh Now
        g.DrawString("Refresh Now", regFont, primaryBrush, new RectangleF(textX, y, textW, itemH), sfLeft);
        y += itemH;

        // Separator 2
        float sepY2 = y + (sepH / 2f);
        g.DrawLine(sepPen, menuRect.X + (12f * s), sepY2, menuRect.Right - (12f * s), sepY2);
        y += sepH;

        // 4. Exit
        g.DrawString("Exit", regFont, primaryBrush, new RectangleF(textX, y, textW, itemH), sfLeft);

        return bmp;
    }

    private static Bitmap RenderTaskbarComparisonSheet(int scaleFactor)
    {
        int s = Math.Clamp(scaleFactor, 1, 3);
        int width = 760 * s;
        int height = 340 * s;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(248, 250, 252));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using var titleFont = new Font("Segoe UI Semibold", 15f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI Semibold", 12f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var capFont = new Font("Segoe UI", 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42));

        g.DrawString(
            $"Tray Icons on Windows 11 Dark (#18181B) vs Light (#F3F4F6) Taskbars ({s}x scale)",
            titleFont,
            inkBrush,
            24f * s,
            16f * s);

        (string Label, Color Bg, Color TextColor, float Y)[] bars =
        [
            ("Windows 11 Dark Taskbar (#18181B)  -  16x16 & 32x32 native renders", Color.FromArgb(24, 24, 27), Color.FromArgb(241, 245, 249), 54f),
            ("Windows 11 Light Taskbar (#F3F4F6)  -  16x16 & 32x32 native renders", Color.FromArgb(243, 244, 246), Color.FromArgb(15, 23, 42), 192f)
        ];

        string[] shortNames =
        [
            "PC + BT Off",
            "PC + BT Standby",
            "PC + BT Connected",
            "Headset Off (Hollow)",
            "Disconnected"
        ];

        foreach (var bar in bars)
        {
            var barRect = new Rectangle(24 * s, (int)(bar.Y * s), 712 * s, 118 * s);
            using (var bgBrush = new SolidBrush(bar.Bg))
            using (var borderPen = new Pen(Color.FromArgb(100, 116, 139), 1f * s))
            using (var barTextBrush = new SolidBrush(bar.TextColor))
            {
                g.FillRectangle(bgBrush, barRect);
                g.DrawRectangle(borderPen, barRect);
                g.DrawString(bar.Label, labelFont, barTextBrush, barRect.X + (16f * s), barRect.Y + (10f * s));

                for (int i = 0; i < TrayIconRenderer.CanonicalExportStates.Length; i++)
                {
                    var (_, key) = TrayIconRenderer.CanonicalExportStates[i];
                    int colX = barRect.X + ((24 + (i * 138)) * s);
                    int rowY = barRect.Y + (42 * s);

                    // Always render true 16x16 & 32x32 icons and upscale with NearestNeighbor when s > 1
                    // so 2x inspection sheets reveal the exact 16x16 pixel grid!
                    using (Bitmap icon16 = TrayIconRenderer.RenderBitmap(key, 16))
                    {
                        g.DrawImage(icon16, new Rectangle(colX, rowY + (8 * s), 16 * s, 16 * s));
                    }

                    using (Bitmap icon32 = TrayIconRenderer.RenderBitmap(key, 32))
                    {
                        g.DrawImage(icon32, new Rectangle(colX + (28 * s), rowY, 32 * s, 32 * s));
                    }

                    g.DrawString(shortNames[i], capFont, barTextBrush, colX - (4f * s), rowY + (42f * s));
                }
            }
        }

        return bmp;
    }

    public static IReadOnlyList<string> GenerateAll(string repoRoot)
    {
        string imagesDir = Path.Combine(repoRoot, "docs", "images");
        string iconsDir = Path.Combine(imagesDir, "icons");
        Directory.CreateDirectory(imagesDir);
        Directory.CreateDirectory(iconsDir);

        var written = new List<string>();

        // 1. Export 48x48 inline transparent icons for Markdown tables
        foreach (var (name, key) in TrayIconRenderer.CanonicalExportStates)
        {
            string iconPath = Path.Combine(iconsDir, $"{name}.png");
            using Bitmap bmp = TrayIconRenderer.RenderBitmap(key, 48);
            bmp.Save(iconPath, ImageFormat.Png);
            written.Add(iconPath);
        }

        // 2. Hero Banner (1280 x 620, 2x supersampled)
        string heroPath = Path.Combine(imagesDir, "hero-banner.png");
        using (Bitmap hero = RenderSupersampled(1280, 620, DrawHeroBanner))
        {
            hero.Save(heroPath, ImageFormat.Png);
        }
        written.Add(heroPath);

        // 3. Visual Guide Cheat Sheet (1200 x 720, 2x supersampled)
        string guidePath = Path.Combine(imagesDir, "visual-guide.png");
        using (Bitmap guide = RenderSupersampled(1200, 720, DrawVisualGuide))
        {
            guide.Save(guidePath, ImageFormat.Png);
        }
        written.Add(guidePath);

        // 4. Annotated Status Card + Taskbar Pinning Guide (1080 x 620, 2x supersampled)
        string annotatedPath = Path.Combine(imagesDir, "status-card-annotated.png");
        using (Bitmap annotated = RenderSupersampled(1080, 620, DrawAnnotatedCard))
        {
            annotated.Save(annotatedPath, ImageFormat.Png);
        }
        written.Add(annotatedPath);

        return written;
    }

    private static Bitmap RenderSupersampled(int width, int height, Action<Graphics> drawLogical)
    {
        const int ss = 2;
        using var hiRes = new Bitmap(width * ss, height * ss, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(hiRes))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(ss, ss);
            drawLogical(g);
        }

        var finalBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics gf = Graphics.FromImage(finalBmp))
        {
            gf.SmoothingMode = SmoothingMode.AntiAlias;
            gf.InterpolationMode = InterpolationMode.HighQualityBicubic;
            gf.PixelOffsetMode = PixelOffsetMode.HighQuality;
            gf.CompositingQuality = CompositingQuality.HighQuality;
            gf.DrawImage(hiRes, new Rectangle(0, 0, width, height));
        }

        return finalBmp;
    }

    private static HeadsetSnapshot CreateShowcaseSnapshot()
    {
        var localDt = new DateTime(2026, 11, 13, 14, 15, 8, DateTimeKind.Unspecified);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(localDt);
        return new HeadsetSnapshot
        {
            Timestamp = new DateTimeOffset(localDt, localOffset),
            PcConnection = PcConnectionState.Connected24Ghz,
            Bluetooth = BluetoothState.Connected,
            BluetoothDetectionSource = BluetoothSource.DongleHidReport,
            DonglePresent = true,
            DongleProductName = "Arctis Nova 7",
            DongleVidPid = "1038:22A1",
            HeadsetOnline24Ghz = true,
            BatteryPercent = 86,
            Charging = BatteryChargingState.Discharging
        };
    }

    private static void DrawHeroBanner(Graphics g)
    {
        const int W = 1280;
        const int H = 620;

        using (var bg = new LinearGradientBrush(
            new Rectangle(0, 0, W, H),
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(238, 243, 249),
            LinearGradientMode.ForwardDiagonal))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        DrawSoftGlow(g, new RectangleF(750, 35, 490, 490), Color.FromArgb(45, 187, 247, 208));
        DrawSoftGlow(g, new RectangleF(60, 330, 520, 260), Color.FromArgb(45, 191, 219, 254));

        using (var framePen = new Pen(Color.FromArgb(203, 213, 225), 1.5f))
        using (GraphicsPath outer = RoundRect(new RectangleF(1f, 1f, W - 2f, H - 2f), 16f))
        {
            g.DrawPath(framePen, outer);
        }

        float leftX = 64f;

        using var eyebrowFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        DrawPill(
            g,
            new RectangleF(leftX, 52f, 344f, 28f),
            "WINDOWS 11 SYSTEM TRAY  ·  LIGHTWEIGHT & NATIVE",
            eyebrowFont,
            Color.FromArgb(239, 246, 255),
            Color.FromArgb(30, 58, 138),
            Color.FromArgb(147, 197, 253));

        using var heroTitleFont = new Font("Segoe UI", 43f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        g.DrawString("Arctis Nova 7", heroTitleFont, inkBrush, leftX - 3f, 96f);
        g.DrawString("System Tray", heroTitleFont, inkBrush, leftX - 3f, 146f);

        using var taglineFont = new Font("Segoe UI", 18.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var slateBrush = new SolidBrush(Color.FromArgb(51, 65, 85));
        g.DrawString(
            "See at a glance if your headset is actually on, connected to your PC, and paired to Bluetooth.",
            taglineFont,
            slateBrush,
            new RectangleF(leftX, 212f, 556f, 66f));

        using var chipFont = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        string[] chips = ["2.4 GHz + Phone BT", "Live Battery %", "Runs Beside GG"];
        float chipX = leftX;
        foreach (string chip in chips)
        {
            float cw = g.MeasureString(chip, chipFont).Width + 24f;
            DrawPill(
                g,
                new RectangleF(chipX, 288f, cw, 30f),
                chip,
                chipFont,
                Color.White,
                Color.FromArgb(30, 41, 59),
                Color.FromArgb(148, 163, 184));
            chipX += cw + 10f;
        }

        using var sectionCapFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("LIVE ON YOUR WINDOWS 11 TASKBAR", sectionCapFont, slateBrush, leftX + 2f, 352f);

        var taskbarRect = new RectangleF(leftX, 376f, 566f, 92f);
        DrawSoftShadow(g, taskbarRect, 16f, 10, 18);
        using (GraphicsPath tbPath = RoundRect(taskbarRect, 16f))
        using (var tbBrush = new LinearGradientBrush(
            taskbarRect,
            Color.FromArgb(32, 32, 36),
            Color.FromArgb(24, 24, 27),
            LinearGradientMode.Vertical))
        using (var tbBorder = new Pen(Color.FromArgb(63, 63, 70), 1.4f))
        {
            g.FillPath(tbBrush, tbPath);
            g.DrawPath(tbBorder, tbPath);
        }

        float cy = taskbarRect.Top + (taskbarRect.Height / 2f);

        using (var chevPen = new Pen(Color.FromArgb(226, 232, 240), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLines(chevPen,
            [
                new PointF(leftX + 30f, cy + 3f),
                new PointF(leftX + 37f, cy - 4f),
                new PointF(leftX + 44f, cy + 3f)
            ]);
        }

        var slotRect = new RectangleF(leftX + 66f, cy - 30f, 60f, 60f);
        using (GraphicsPath slotPath = RoundRect(slotRect, 11f))
        using (var slotFill = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
        using (var slotRim = new Pen(Color.FromArgb(96, 165, 250), 1.5f))
        {
            g.FillPath(slotFill, slotPath);
            g.DrawPath(slotRim, slotPath);
        }

        var activeKey = new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected);
        using (Bitmap trayIconBmp = TrayIconRenderer.RenderBitmap(activeKey, 96))
        {
            g.DrawImage(trayIconBmp, slotRect.X + 9f, slotRect.Y + 9f, 42f, 42f);
        }

        Color trayGlyphColor = Color.FromArgb(241, 245, 249);
        DrawWifiGlyph(g, leftX + 168f, cy, trayGlyphColor);
        DrawSpeakerGlyph(g, leftX + 220f, cy, trayGlyphColor);
        DrawBatteryGlyph(g, leftX + 274f, cy, trayGlyphColor);

        using var clockFont = new Font("Segoe UI Semibold", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var dateFont = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var tbClockBrush = new SolidBrush(Color.FromArgb(248, 250, 252));
        using var tbDateBrush = new SolidBrush(Color.FromArgb(203, 213, 225));
        var sfRight = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString("2:15 PM", clockFont, tbClockBrush, new RectangleF(taskbarRect.Right - 150f, cy - 19f, 128f, 22f), sfRight);
        g.DrawString("11/13/2026", dateFont, tbDateBrush, new RectangleF(taskbarRect.Right - 150f, cy + 2f, 128f, 20f), sfRight);

        using var captionFont = new Font("Segoe UI Semibold", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var greenBrush = new SolidBrush(Color.FromArgb(22, 163, 74));
        using var deepGreenBrush = new SolidBrush(Color.FromArgb(20, 83, 45));
        g.FillEllipse(greenBrush, leftX + 68f, 487f, 8f, 8f);
        g.DrawString(
            "Green headset + blue badge: awake on 2.4 GHz and connected to Bluetooth",
            captionFont,
            deepGreenBrush,
            leftX + 82f,
            481f);

        // Right column: Actual Light Mode StatusFlyoutForm rendered at exact 436:392 aspect ratio (506 x 455)
        var cardTarget = new RectangleF(708f, 68f, 506f, 455f);
        DrawSoftShadow(g, cardTarget, 12f, 18, 24);

        HeadsetSnapshot heroSnapshot = CreateShowcaseSnapshot();
        using (Bitmap cardBmp = StatusFlyoutForm.RenderCardBitmap(
            heroSnapshot,
            scale: 2.4f,
            referenceNow: heroSnapshot.Timestamp.AddSeconds(2)))
        using (GraphicsPath clipPath = RoundRect(cardTarget, 12f))
        {
            GraphicsState saved = g.Save();
            g.SetClip(clipPath);
            g.DrawImage(cardBmp, cardTarget);
            g.Restore(saved);

            using var cardOutline = new Pen(Color.FromArgb(100, 116, 139), 1.4f);
            g.DrawPath(cardOutline, clipPath);
        }

        using var cardFootFont = new Font("Segoe UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(
            "Left-click the tray icon anytime to open the live Status Card",
            cardFootFont,
            slateBrush,
            new RectangleF(cardTarget.X - 10f, cardTarget.Bottom + 16f, cardTarget.Width + 20f, 24f),
            sfCenter);
    }

    private static void DrawVisualGuide(Graphics g)
    {
        const int W = 1200;
        const int H = 720;

        using (var bg = new LinearGradientBrush(
            new Rectangle(0, 0, W, H),
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(241, 245, 249),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        using (var framePen = new Pen(Color.FromArgb(203, 213, 225), 1.5f))
        using (GraphicsPath outer = RoundRect(new RectangleF(1f, 1f, W - 2f, H - 2f), 16f))
        {
            g.DrawPath(framePen, outer);
        }

        using var h1Font = new Font("Segoe UI", 27f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var rowTitleFont = new Font("Segoe UI Semibold", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var rowDescFont = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var pillFont = new Font("Segoe UI Semibold", 12f, FontStyle.Bold, GraphicsUnit.Pixel);

        using var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        using var slateBrush = new SolidBrush(Color.FromArgb(51, 65, 85));

        g.DrawString("Tray Icon Visual Cheat Sheet", h1Font, inkBrush, 46f, 24f);
        g.DrawString(
            "Two visual layers tell the whole story: headset color shows your PC link, and the corner badge shows Bluetooth.",
            subFont,
            slateBrush,
            48f,
            64f);

        (TrayVisualKey Key, string Title, string Desc, string Pill, Color Accent, Color PillBg, Color PillFg, Color PillBorder)[] rows =
        [
            (
                new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected),
                "PC + Bluetooth Connected",
                "Awake on 2.4 GHz (or PC Bluetooth) and actively connected to your phone or PC over Bluetooth.",
                "2.4 GHz + BT Active",
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(219, 234, 254),
                Color.FromArgb(30, 58, 138),
                Color.FromArgb(29, 78, 216)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Standby),
                "PC Connected, Bluetooth Waiting",
                "Awake on 2.4 GHz; headset Bluetooth radio is switched on and waiting to connect.",
                "2.4 GHz + BT Waiting",
                Color.FromArgb(59, 130, 246),
                Color.FromArgb(239, 246, 255),
                Color.FromArgb(30, 58, 138),
                Color.FromArgb(37, 99, 235)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Off),
                "PC Connected Only",
                "Awake on 2.4 GHz wireless; headset Bluetooth radio is turned off.",
                "2.4 GHz Only",
                Color.FromArgb(22, 163, 74),
                Color.FromArgb(220, 252, 231),
                Color.FromArgb(20, 83, 45),
                Color.FromArgb(21, 128, 61)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.DongleOnlyHeadsetOff, BluetoothBadgeVisual.Off),
                "Headset Off, Receiver Plugged In",
                "USB-C wireless receiver is plugged into your PC, but the headset is powered off or asleep.",
                "Off or Asleep",
                Color.FromArgb(217, 119, 6),
                Color.FromArgb(254, 243, 199),
                Color.FromArgb(120, 53, 15),
                Color.FromArgb(180, 83, 9)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.Disconnected, BluetoothBadgeVisual.Off),
                "Disconnected",
                "USB-C wireless receiver is unplugged and the headset is not connected to this PC over Bluetooth.",
                "Unplugged / Offline",
                Color.FromArgb(220, 38, 38),
                Color.FromArgb(254, 226, 226),
                Color.FromArgb(127, 29, 29),
                Color.FromArgb(220, 38, 38)
            )
        ];

        float y = 110f;
        const float cardX = 48f;
        const float cardW = 1104f;
        const float cardH = 102f;
        const float gap = 14f;

        foreach (var row in rows)
        {
            var rect = new RectangleF(cardX, y, cardW, cardH);
            DrawSoftShadow(g, rect, 12f, 6, 10);

            using (GraphicsPath path = RoundRect(rect, 12f))
            using (var white = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(203, 213, 225), 1.2f))
            {
                g.FillPath(white, path);

                GraphicsState st = g.Save();
                g.SetClip(path);
                using (var accentBrush = new SolidBrush(row.Accent))
                {
                    g.FillRectangle(accentBrush, rect.X, rect.Y, 6f, rect.Height);
                }
                g.Restore(st);

                g.DrawPath(border, path);
            }

            var tileRect = new RectangleF(rect.X + 22f, rect.Y + 15f, 72f, 72f);
            using (GraphicsPath tilePath = RoundRect(tileRect, 12f))
            using (var tileFill = new SolidBrush(Color.FromArgb(24, 24, 27)))
            using (var tileBorder = new Pen(Color.FromArgb(100, 116, 139), 1f))
            {
                g.FillPath(tileFill, tilePath);
                g.DrawPath(tileBorder, tilePath);
            }

            using (Bitmap iconBmp = TrayIconRenderer.RenderBitmap(row.Key, 112))
            {
                g.DrawImage(iconBmp, tileRect.X + 8f, tileRect.Y + 8f, 56f, 56f);
            }

            float pillW = g.MeasureString(row.Pill, pillFont).Width + 24f;
            var pillRect = new RectangleF(rect.Right - 22f - pillW, rect.Y + 20f, pillW, 26f);
            DrawPill(g, pillRect, row.Pill, pillFont, row.PillBg, row.PillFg, row.PillBorder);

            float textX = tileRect.Right + 20f;
            g.DrawString(row.Title, rowTitleFont, inkBrush, textX, rect.Y + 20f);
            g.DrawString(row.Desc, rowDescFont, slateBrush, new RectangleF(textX, rect.Y + 54f, rect.Right - textX - 24f, 36f));

            y += cardH + gap;
        }
    }

    private static void DrawAnnotatedCard(Graphics g)
    {
        const int W = 1080;
        const int H = 620;

        using (var bg = new LinearGradientBrush(
            new Rectangle(0, 0, W, H),
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(241, 245, 249),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        using (var framePen = new Pen(Color.FromArgb(203, 213, 225), 1.5f))
        using (GraphicsPath outer = RoundRect(new RectangleF(1f, 1f, W - 2f, H - 2f), 16f))
        {
            g.DrawPath(framePen, outer);
        }

        using var h1Font = new Font("Segoe UI", 25f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", 14.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        using var slateBrush = new SolidBrush(Color.FromArgb(51, 65, 85));

        g.DrawString("Status Card Tour & Taskbar Pinning", h1Font, inkBrush, 46f, 22f);
        g.DrawString(
            "Every detail in the popup card maps directly to live USB HID and Windows Bluetooth state.",
            subFont,
            slateBrush,
            48f,
            58f);

        // Render Status Card at left (408 x 367 logical -> exact 436:392 aspect ratio, k = 0.9362)
        var cardRect = new RectangleF(46f, 96f, 408f, 367f);
        DrawSoftShadow(g, cardRect, 12f, 14, 18);

        HeadsetSnapshot snap = CreateShowcaseSnapshot();
        using (Bitmap cardBmp = StatusFlyoutForm.RenderCardBitmap(
            snap,
            scale: 2.0f,
            referenceNow: snap.Timestamp.AddSeconds(2)))
        using (GraphicsPath cardClip = RoundRect(cardRect, 12f))
        {
            GraphicsState st = g.Save();
            g.SetClip(cardClip);
            g.DrawImage(cardBmp, cardRect);
            g.Restore(st);

            using var borderPen = new Pen(Color.FromArgb(100, 116, 139), 1.3f);
            g.DrawPath(borderPen, cardClip);
        }

        float k = cardRect.Height / StatusFlyoutForm.LogicalHeight;
        (float CardAnchorY, string Title, string Body)[] callouts =
        [
            (cardRect.Top + (30f * k), "Live Headset Icon, Pill & Close", "Matches your taskbar icon, shows PC status, and closes with 'x' or Esc."),
            (cardRect.Top + (107f * k), "Headset Wireless Link", "Tells you if the headset is actually on, not just sitting off on your desk."),
            (cardRect.Top + (161f * k), "USB-C Receiver Presence", "Confirms the SteelSeries receiver is plugged in and active or waiting."),
            (cardRect.Top + (215f * k), "Phone or PC Bluetooth", "Reads Bluetooth state from the headset, even when paired to your phone."),
            (cardRect.Top + (294f * k), "Battery Gauge & Tactile Refresh", "High-contrast battery bar, live timestamp, and tactile Refresh (or F5).")
        ];

        using var callTitleFont = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var callBodyFont = new Font("Segoe UI", 12.8f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var numFont = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var connectorPen = new Pen(Color.FromArgb(37, 99, 235), 1.8f);
        using var blueBrush = new SolidBrush(Color.FromArgb(37, 99, 235));

        float callY = 96f;
        const float callX = 516f;
        const float callW = 516f;
        const float callH = 68f;
        const float callGap = 7.2f;

        for (int i = 0; i < callouts.Length; i++)
        {
            var item = callouts[i];
            var box = new RectangleF(callX, callY, callW, callH);
            float boxMidY = box.Top + (box.Height / 2f);

            PointF pStart = new(cardRect.Right + 2f, item.CardAnchorY);
            PointF pMid1 = new(cardRect.Right + 30f, item.CardAnchorY);
            PointF pMid2 = new(cardRect.Right + 30f, boxMidY);
            PointF pEnd = new(box.Left, boxMidY);
            g.DrawLines(connectorPen, [pStart, pMid1, pMid2, pEnd]);

            using (var whiteMoat = new SolidBrush(Color.White))
            {
                g.FillEllipse(whiteMoat, pStart.X - 5f, pStart.Y - 5f, 10f, 10f);
                g.FillEllipse(blueBrush, pStart.X - 3.5f, pStart.Y - 3.5f, 7f, 7f);
            }

            using (GraphicsPath boxPath = RoundRect(box, 10f))
            using (var boxFill = new SolidBrush(Color.White))
            using (var boxBorder = new Pen(Color.FromArgb(203, 213, 225), 1.1f))
            {
                g.FillPath(boxFill, boxPath);
                g.DrawPath(boxBorder, boxPath);
            }

            var numRect = new RectangleF(box.X + 12f, boxMidY - 13f, 26f, 26f);
            g.FillEllipse(blueBrush, numRect);
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString((i + 1).ToString(), numFont, Brushes.White, numRect, sfCenter);

            float tx = numRect.Right + 11f;
            g.DrawString(item.Title, callTitleFont, inkBrush, tx, box.Y + 10f);
            g.DrawString(item.Body, callBodyFont, slateBrush, new RectangleF(tx, box.Y + 32f, box.Right - tx - 10f, 32f));

            callY += callH + callGap;
        }

        var tipRect = new RectangleF(48f, 492f, 984f, 98f);
        using (GraphicsPath tipPath = RoundRect(tipRect, 12f))
        using (var tipFill = new SolidBrush(Color.FromArgb(239, 246, 255)))
        using (var tipBorder = new Pen(Color.FromArgb(147, 197, 253), 1.3f))
        {
            g.FillPath(tipFill, tipPath);
            g.DrawPath(tipBorder, tipPath);
        }

        float tipMidY = tipRect.Top + (tipRect.Height / 2f);
        var overflowMini = new RectangleF(68f, tipMidY - 22f, 46f, 44f);
        using (GraphicsPath ovPath = RoundRect(overflowMini, 8f))
        using (var darkTb = new SolidBrush(Color.FromArgb(24, 24, 27)))
        using (var rim = new Pen(Color.FromArgb(100, 116, 139), 1.2f))
        {
            g.FillPath(darkTb, ovPath);
            g.DrawPath(rim, ovPath);
            using var chevPen = new Pen(Color.FromArgb(241, 245, 249), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(chevPen,
            [
                new PointF(overflowMini.X + 16f, tipMidY + 3f),
                new PointF(overflowMini.X + 23f, tipMidY - 4f),
                new PointF(overflowMini.X + 30f, tipMidY + 3f)
            ]);
        }

        using (var arrowPen = new Pen(Color.FromArgb(37, 99, 235), 2.0f) { DashStyle = DashStyle.Dash, EndCap = LineCap.ArrowAnchor })
        {
            g.DrawLine(arrowPen, overflowMini.Right + 8f, tipMidY, overflowMini.Right + 54f, tipMidY);
        }

        var pinnedMini = new RectangleF(overflowMini.Right + 62f, tipMidY - 22f, 112f, 44f);
        using (GraphicsPath pinPath = RoundRect(pinnedMini, 8f))
        using (var darkTb = new SolidBrush(Color.FromArgb(24, 24, 27)))
        using (var rim = new Pen(Color.FromArgb(100, 116, 139), 1.2f))
        {
            g.FillPath(darkTb, pinPath);
            g.DrawPath(rim, pinPath);
        }

        using (Bitmap miniIcon = TrayIconRenderer.RenderBitmap(new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected), 64))
        {
            g.DrawImage(miniIcon, pinnedMini.X + 10f, tipMidY - 15f, 30f, 30f);
        }
        using var pinTimeFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var whiteText = new SolidBrush(Color.FromArgb(248, 250, 252));
        g.DrawString("2:15 PM", pinTimeFont, whiteText, pinnedMini.X + 48f, tipMidY - 8f);

        using var tipTitleFont = new Font("Segoe UI Semibold", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tipBodyFont = new Font("Segoe UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var navyBrush = new SolidBrush(Color.FromArgb(30, 58, 138));
        float tipTextX = pinnedMini.Right + 22f;
        g.DrawString("First-Run Tip: Pin the Headset Icon to Your Taskbar", tipTitleFont, navyBrush, tipTextX, tipRect.Y + 18f);
        g.DrawString(
            "Windows 11 places newly launched tray icons inside the '^' hidden icons menu by default. Click '^' near your clock and drag the headset icon onto your taskbar once so it stays visible.",
            tipBodyFont,
            slateBrush,
            new RectangleF(tipTextX, tipRect.Y + 44f, tipRect.Right - tipTextX - 18f, 44f));
    }

    private static void DrawPill(
        Graphics g,
        RectangleF bounds,
        string text,
        Font font,
        Color bg,
        Color fg,
        Color border)
    {
        using GraphicsPath path = RoundRect(bounds, bounds.Height / 2f);
        using var fillBrush = new SolidBrush(bg);
        using var borderPen = new Pen(border, 1.1f);
        using var textBrush = new SolidBrush(fg);
        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, textBrush, bounds, sf);
    }

    private static void DrawSoftShadow(Graphics g, RectangleF target, float radius, int layers, int maxAlpha)
    {
        for (int i = layers; i >= 1; i--)
        {
            float spread = i * 1.4f;
            float offsetY = i * 0.7f;
            int alpha = Math.Max(2, (maxAlpha * (layers - i + 1)) / (layers * 2));
            var r = new RectangleF(target.X - spread, target.Y - (spread * 0.5f) + offsetY, target.Width + (spread * 2f), target.Height + (spread * 1.8f));
            using GraphicsPath p = RoundRect(r, radius + spread);
            using var b = new SolidBrush(Color.FromArgb(alpha, 15, 23, 42));
            g.FillPath(b, p);
        }
    }

    private static void DrawSoftGlow(Graphics g, RectangleF bounds, Color centerColor)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        using var pgb = new PathGradientBrush(path)
        {
            CenterColor = centerColor,
            SurroundColors = [Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B)]
        };
        g.FillPath(pgb, path);
    }

    private static void DrawWifiGlyph(Graphics g, float cx, float cy, Color color)
    {
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        g.DrawArc(pen, cx - 10f, cy - 8f, 20f, 20f, 220f, 100f);
        g.DrawArc(pen, cx - 6.5f, cy - 4.2f, 13f, 13f, 220f, 100f);
        g.FillEllipse(brush, cx - 2f, cy + 4f, 4f, 4f);
    }

    private static void DrawSpeakerGlyph(Graphics g, float cx, float cy, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        PointF[] body =
        [
            new(cx - 8f, cy - 3.5f),
            new(cx - 4.5f, cy - 3.5f),
            new(cx, cy - 7.5f),
            new(cx, cy + 7.5f),
            new(cx - 4.5f, cy + 3.5f),
            new(cx - 8f, cy + 3.5f)
        ];
        g.FillPolygon(brush, body);
        g.DrawArc(pen, cx - 1f, cy - 4.5f, 7f, 9f, -55f, 110f);
        g.DrawArc(pen, cx - 2f, cy - 7.5f, 12f, 15f, -55f, 110f);
    }

    private static void DrawBatteryGlyph(Graphics g, float cx, float cy, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        using var brush = new SolidBrush(color);
        using GraphicsPath body = RoundRect(new RectangleF(cx - 10f, cy - 5.5f, 18f, 11f), 2.5f);
        g.DrawPath(pen, body);
        g.FillRectangle(brush, cx - 7.5f, cy - 3f, 11.5f, 6f);
        g.FillRectangle(brush, cx + 8.5f, cy - 2.5f, 2f, 5f);
    }

    private static GraphicsPath RoundRect(RectangleF bounds, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180f, 90f);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270f, 90f);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
