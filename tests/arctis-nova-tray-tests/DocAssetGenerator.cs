using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ArctisNovaTray.Models;
using ArctisNovaTray.Rendering;
using ArctisNovaTray.Ui;

namespace ArctisNovaTray.Tests;

internal static class DocAssetGenerator
{
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

        // 4. Annotated Status Card + Taskbar Pinning Guide (1080 x 600, 2x supersampled)
        string annotatedPath = Path.Combine(imagesDir, "status-card-annotated.png");
        using (Bitmap annotated = RenderSupersampled(1080, 600, DrawAnnotatedCard))
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

        // Soft light Windows 11 studio canvas
        using (var bg = new LinearGradientBrush(
            new Rectangle(0, 0, W, H),
            Color.FromArgb(248, 250, 252), // #F8FAFC
            Color.FromArgb(238, 243, 249),
            LinearGradientMode.ForwardDiagonal))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        // Subtle soft ambient color washes
        DrawSoftGlow(g, new RectangleF(760, 40, 480, 480), Color.FromArgb(45, 187, 247, 208));
        DrawSoftGlow(g, new RectangleF(60, 330, 520, 260), Color.FromArgb(45, 191, 219, 254));

        using (var framePen = new Pen(Color.FromArgb(226, 232, 240), 1.5f))
        using (GraphicsPath outer = RoundRect(new RectangleF(1f, 1f, W - 2f, H - 2f), 16f))
        {
            g.DrawPath(framePen, outer);
        }

        float leftX = 64f;

        // Eyebrow pill
        using var eyebrowFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        DrawPill(
            g,
            new RectangleF(leftX, 54f, 344f, 28f),
            "WINDOWS 11 SYSTEM TRAY  ·  LIGHTWEIGHT & NATIVE",
            eyebrowFont,
            Color.FromArgb(239, 246, 255),
            Color.FromArgb(29, 78, 216),
            Color.FromArgb(191, 219, 254));

        // Title
        using var heroTitleFont = new Font("Segoe UI", 43f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        g.DrawString("Arctis Nova 7", heroTitleFont, inkBrush, leftX - 3f, 98f);
        g.DrawString("System Tray", heroTitleFont, inkBrush, leftX - 3f, 148f);

        // Tagline
        using var taglineFont = new Font("Segoe UI", 18.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var slateBrush = new SolidBrush(Color.FromArgb(71, 85, 105));
        g.DrawString(
            "See at a glance if your headset is actually on, connected to your PC, and paired to Bluetooth.",
            taglineFont,
            slateBrush,
            new RectangleF(leftX, 214f, 556f, 66f));

        // 3 compact feature chips
        using var chipFont = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        string[] chips = ["2.4GHz + Phone BT", "Live Battery %", "Runs Beside GG"];
        float chipX = leftX;
        foreach (string chip in chips)
        {
            float cw = g.MeasureString(chip, chipFont).Width + 24f;
            DrawPill(
                g,
                new RectangleF(chipX, 290f, cw, 30f),
                chip,
                chipFont,
                Color.White,
                Color.FromArgb(51, 65, 85),
                Color.FromArgb(203, 213, 225));
            chipX += cw + 10f;
        }

        // Zoomed-in Windows 11 Light Taskbar Strip
        using var sectionCapFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139));
        g.DrawString("LIVE ON YOUR WINDOWS 11 TASKBAR", sectionCapFont, mutedBrush, leftX + 2f, 354f);

        var taskbarRect = new RectangleF(leftX, 378f, 566f, 92f);
        DrawSoftShadow(g, taskbarRect, 16f, 10, 16);
        using (GraphicsPath tbPath = RoundRect(taskbarRect, 16f))
        using (var tbBrush = new LinearGradientBrush(
            taskbarRect,
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(241, 245, 249),
            LinearGradientMode.Vertical))
        using (var tbBorder = new Pen(Color.FromArgb(203, 213, 225), 1.4f))
        {
            g.FillPath(tbBrush, tbPath);
            g.DrawPath(tbBorder, tbPath);
        }

        float cy = taskbarRect.Top + (taskbarRect.Height / 2f);

        // Overflow ^ chevron
        using (var chevPen = new Pen(Color.FromArgb(71, 85, 105), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLines(chevPen,
            [
                new PointF(leftX + 30f, cy + 3f),
                new PointF(leftX + 37f, cy - 4f),
                new PointF(leftX + 44f, cy + 3f)
            ]);
        }

        // Highlighted headset tray slot
        var slotRect = new RectangleF(leftX + 66f, cy - 30f, 60f, 60f);
        using (GraphicsPath slotPath = RoundRect(slotRect, 11f))
        using (var slotFill = new SolidBrush(Color.FromArgb(239, 246, 255)))
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

        // System tray neighbor icons (Wi-Fi, Volume, PC Battery)
        DrawWifiGlyph(g, leftX + 168f, cy, Color.FromArgb(51, 65, 85));
        DrawSpeakerGlyph(g, leftX + 220f, cy, Color.FromArgb(51, 65, 85));
        DrawBatteryGlyph(g, leftX + 274f, cy, Color.FromArgb(51, 65, 85));

        // Clock & date stack on right of taskbar strip
        using var clockFont = new Font("Segoe UI Semibold", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var dateFont = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        var sfRight = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString("2:15 PM", clockFont, inkBrush, new RectangleF(taskbarRect.Right - 150f, cy - 19f, 128f, 22f), sfRight);
        g.DrawString("11/13/2026", dateFont, slateBrush, new RectangleF(taskbarRect.Right - 150f, cy + 2f, 128f, 20f), sfRight);

        // Taskbar callout caption underneath
        using var captionFont = new Font("Segoe UI Semibold", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var greenBrush = new SolidBrush(Color.FromArgb(22, 163, 74));
        using var deepGreenBrush = new SolidBrush(Color.FromArgb(21, 128, 61));
        g.FillEllipse(greenBrush, leftX + 68f, 487f, 8f, 8f);
        g.DrawString(
            "Green headset + blue badge: awake on 2.4GHz and connected to Bluetooth",
            captionFont,
            deepGreenBrush,
            leftX + 82f,
            481f);

        // Right column: Actual Light Mode StatusFlyoutForm rendered at high DPI
        var cardTarget = new RectangleF(712f, 84f, 498f, 437f);
        DrawSoftShadow(g, cardTarget, 12f, 18, 24);

        HeadsetSnapshot heroSnapshot = CreateShowcaseSnapshot();
        using (Bitmap cardBmp = StatusFlyoutForm.RenderCardBitmap(heroSnapshot, scale: 2.8f))
        using (GraphicsPath clipPath = RoundRect(cardTarget, 12f))
        {
            GraphicsState saved = g.Save();
            g.SetClip(clipPath);
            g.DrawImage(cardBmp, cardTarget);
            g.Restore(saved);

            using var cardOutline = new Pen(Color.FromArgb(203, 213, 225), 1.4f);
            g.DrawPath(cardOutline, clipPath);
        }

        using var cardFootFont = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(
            "Left-click the tray icon anytime (or launch the app) to pop open the live Status Card",
            cardFootFont,
            mutedBrush,
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

        using (var framePen = new Pen(Color.FromArgb(226, 232, 240), 1.5f))
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
        using var slateBrush = new SolidBrush(Color.FromArgb(71, 85, 105));

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
                "Awake on 2.4GHz (or PC Bluetooth) and actively connected to your phone or PC over Bluetooth.",
                "2.4GHz + BT Active",
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(219, 234, 254),
                Color.FromArgb(30, 64, 175),
                Color.FromArgb(147, 197, 253)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Standby),
                "PC Connected, Bluetooth Standby",
                "Awake on 2.4GHz; headset Bluetooth radio is switched on and waiting to connect.",
                "2.4GHz + BT Standby",
                Color.FromArgb(59, 130, 246),
                Color.FromArgb(239, 246, 255),
                Color.FromArgb(29, 78, 216),
                Color.FromArgb(191, 219, 254)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Off),
                "PC Connected Only",
                "Awake on 2.4GHz wireless; headset Bluetooth radio is turned off.",
                "2.4GHz Only",
                Color.FromArgb(22, 163, 74),
                Color.FromArgb(220, 252, 231),
                Color.FromArgb(22, 101, 52),
                Color.FromArgb(134, 239, 172)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.DongleOnlyHeadsetOff, BluetoothBadgeVisual.Off),
                "Headset Off, Dongle Plugged In",
                "USB-C wireless receiver is plugged into your PC, but the headset is powered off or asleep.",
                "Headset Off / Asleep",
                Color.FromArgb(217, 119, 6),
                Color.FromArgb(254, 243, 199),
                Color.FromArgb(146, 64, 14),
                Color.FromArgb(252, 211, 77)
            ),
            (
                new TrayVisualKey(HeadsetBaseVisual.Disconnected, BluetoothBadgeVisual.Off),
                "Disconnected",
                "USB-C wireless receiver is unplugged and the headset is not connected to this PC over Bluetooth.",
                "Unplugged / Offline",
                Color.FromArgb(220, 38, 38),
                Color.FromArgb(254, 226, 226),
                Color.FromArgb(153, 27, 27),
                Color.FromArgb(252, 165, 165)
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

                // Left accent bar clipped cleanly inside card
                GraphicsState st = g.Save();
                g.SetClip(path);
                using (var accentBrush = new SolidBrush(row.Accent))
                {
                    g.FillRectangle(accentBrush, rect.X, rect.Y, 6f, rect.Height);
                }
                g.Restore(st);

                g.DrawPath(border, path);
            }

            // Icon tile
            var tileRect = new RectangleF(rect.X + 22f, rect.Y + 15f, 72f, 72f);
            using (GraphicsPath tilePath = RoundRect(tileRect, 12f))
            using (var tileFill = new SolidBrush(Color.FromArgb(248, 250, 252)))
            using (var tileBorder = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                g.FillPath(tileFill, tilePath);
                g.DrawPath(tileBorder, tilePath);
            }

            using (Bitmap iconBmp = TrayIconRenderer.RenderBitmap(row.Key, 112))
            {
                g.DrawImage(iconBmp, tileRect.X + 8f, tileRect.Y + 8f, 56f, 56f);
            }

            // Right pill
            float pillW = g.MeasureString(row.Pill, pillFont).Width + 24f;
            var pillRect = new RectangleF(rect.Right - 22f - pillW, rect.Y + 20f, pillW, 26f);
            DrawPill(g, pillRect, row.Pill, pillFont, row.PillBg, row.PillFg, row.PillBorder);

            // Title & description
            float textX = tileRect.Right + 20f;
            g.DrawString(row.Title, rowTitleFont, inkBrush, textX, rect.Y + 20f);
            g.DrawString(row.Desc, rowDescFont, slateBrush, new RectangleF(textX, rect.Y + 54f, rect.Right - textX - 24f, 36f));

            y += cardH + gap;
        }
    }

    private static void DrawAnnotatedCard(Graphics g)
    {
        const int W = 1080;
        const int H = 600;

        using (var bg = new LinearGradientBrush(
            new Rectangle(0, 0, W, H),
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(241, 245, 249),
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        using (var framePen = new Pen(Color.FromArgb(226, 232, 240), 1.5f))
        using (GraphicsPath outer = RoundRect(new RectangleF(1f, 1f, W - 2f, H - 2f), 16f))
        {
            g.DrawPath(framePen, outer);
        }

        using var h1Font = new Font("Segoe UI", 25f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", 14.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var inkBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        using var slateBrush = new SolidBrush(Color.FromArgb(71, 85, 105));

        g.DrawString("Status Card Tour & Taskbar Pinning", h1Font, inkBrush, 46f, 22f);
        g.DrawString(
            "Every detail in the popup card maps directly to live USB HID and Windows Bluetooth state.",
            subFont,
            slateBrush,
            48f,
            58f);

        // Render Status Card at left (399 x 350 logical)
        var cardRect = new RectangleF(48f, 96f, 399f, 350f);
        DrawSoftShadow(g, cardRect, 12f, 14, 18);

        using (Bitmap cardBmp = StatusFlyoutForm.RenderCardBitmap(CreateShowcaseSnapshot(), scale: 2.24f))
        using (GraphicsPath cardClip = RoundRect(cardRect, 12f))
        {
            GraphicsState st = g.Save();
            g.SetClip(cardClip);
            g.DrawImage(cardBmp, cardRect);
            g.Restore(st);

            using var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1.3f);
            g.DrawPath(borderPen, cardClip);
        }

        // 5 Monotonic non-crossing callouts from card right edge (x=447) into legend column (x=516..1032)
        (float CardAnchorY, string Title, string Body)[] callouts =
        [
            (137f, "Live Headset Icon & Status Pill", "Matches your taskbar icon and summarizes overall PC link state."),
            (199f, "PC 2.4GHz Wireless Link", "Tells you if the headset is actually on, not just sitting off on your desk."),
            (252f, "USB-C Receiver Presence", "Confirms the SteelSeries dongle is detected on USB and paired."),
            (305f, "Phone or PC Bluetooth", "Reads Bluetooth state from the headset, even when paired to your phone."),
            (362f, "Battery Gauge & Manual Refresh", "Color-coded battery bar, charging state, timestamp, and instant Refresh.")
        ];

        using var callTitleFont = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var callBodyFont = new Font("Segoe UI", 12.8f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var numFont = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var connectorPen = new Pen(Color.FromArgb(59, 130, 246), 1.8f);
        using var blueBrush = new SolidBrush(Color.FromArgb(37, 99, 235));

        float callY = 96f;
        const float callX = 516f;
        const float callW = 516f;
        const float callH = 66f;
        const float callGap = 6.5f;

        for (int i = 0; i < callouts.Length; i++)
        {
            var item = callouts[i];
            var box = new RectangleF(callX, callY, callW, callH);
            float boxMidY = box.Top + (box.Height / 2f);

            // Connector line from card right edge to callout box left edge
            PointF pStart = new(cardRect.Right + 2f, item.CardAnchorY);
            PointF pMid1 = new(cardRect.Right + 32f, item.CardAnchorY);
            PointF pMid2 = new(cardRect.Right + 32f, boxMidY);
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

            // Number circle badge
            var numRect = new RectangleF(box.X + 12f, boxMidY - 13f, 26f, 26f);
            g.FillEllipse(blueBrush, numRect);
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString((i + 1).ToString(), numFont, Brushes.White, numRect, sfCenter);

            float tx = numRect.Right + 11f;
            g.DrawString(item.Title, callTitleFont, inkBrush, tx, box.Y + 9f);
            g.DrawString(item.Body, callBodyFont, slateBrush, new RectangleF(tx, box.Y + 31f, box.Right - tx - 10f, 32f));

            callY += callH + callGap;
        }

        // Bottom Windows 11 Taskbar Overflow Pinning Tip Banner
        var tipRect = new RectangleF(48f, 476f, 984f, 96f);
        using (GraphicsPath tipPath = RoundRect(tipRect, 12f))
        using (var tipFill = new SolidBrush(Color.FromArgb(239, 246, 255)))
        using (var tipBorder = new Pen(Color.FromArgb(191, 219, 254), 1.3f))
        {
            g.FillPath(tipFill, tipPath);
            g.DrawPath(tipBorder, tipPath);
        }

        // Mini drag diagram on left of tip banner
        float tipMidY = tipRect.Top + (tipRect.Height / 2f);
        var overflowMini = new RectangleF(68f, tipMidY - 22f, 46f, 44f);
        using (GraphicsPath ovPath = RoundRect(overflowMini, 8f))
        using (var white = new SolidBrush(Color.White))
        using (var rim = new Pen(Color.FromArgb(147, 197, 253), 1.2f))
        {
            g.FillPath(white, ovPath);
            g.DrawPath(rim, ovPath);
            using var chevPen = new Pen(Color.FromArgb(30, 64, 175), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(chevPen,
            [
                new PointF(overflowMini.X + 16f, tipMidY + 3f),
                new PointF(overflowMini.X + 23f, tipMidY - 4f),
                new PointF(overflowMini.X + 30f, tipMidY + 3f)
            ]);
        }

        // Dashed drag arrow -> pinned icon pill
        using (var arrowPen = new Pen(Color.FromArgb(37, 99, 235), 2.0f) { DashStyle = DashStyle.Dash, EndCap = LineCap.ArrowAnchor })
        {
            g.DrawLine(arrowPen, overflowMini.Right + 8f, tipMidY, overflowMini.Right + 54f, tipMidY);
        }

        var pinnedMini = new RectangleF(overflowMini.Right + 62f, tipMidY - 22f, 108f, 44f);
        using (GraphicsPath pinPath = RoundRect(pinnedMini, 8f))
        using (var white = new SolidBrush(Color.White))
        using (var rim = new Pen(Color.FromArgb(147, 197, 253), 1.2f))
        {
            g.FillPath(white, pinPath);
            g.DrawPath(rim, pinPath);
        }

        using (Bitmap miniIcon = TrayIconRenderer.RenderBitmap(new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected), 64))
        {
            g.DrawImage(miniIcon, pinnedMini.X + 10f, tipMidY - 15f, 30f, 30f);
        }
        using var pinTimeFont = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("2:15 PM", pinTimeFont, inkBrush, pinnedMini.X + 48f, tipMidY - 8f);

        // Tip text on right
        using var tipTitleFont = new Font("Segoe UI Semibold", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tipBodyFont = new Font("Segoe UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var navyBrush = new SolidBrush(Color.FromArgb(30, 58, 138));
        float tipTextX = pinnedMini.Right + 22f;
        g.DrawString("First-Run Tip: Pin the Headset Icon to Your Taskbar", tipTitleFont, navyBrush, tipTextX, tipRect.Y + 18f);
        g.DrawString(
            "Windows 11 places newly launched tray icons inside the '^' hidden icons menu by default. Click '^' near your clock and drag the headset icon onto your taskbar once so it stays visible.",
            tipBodyFont,
            slateBrush,
            new RectangleF(tipTextX, tipRect.Y + 43f, tipRect.Right - tipTextX - 18f, 44f));
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
