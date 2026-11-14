using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ArctisNovaTray.Models;

namespace ArctisNovaTray.Rendering;

public static partial class TrayIconRenderer
{
    public static readonly int[] StandardExportSizes = [16, 32, 64];

    public static readonly (string Name, TrayVisualKey Key)[] CanonicalExportStates =
    [
        ("pc-connected-bt-off", new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Off)),
        ("pc-connected-bt-on", new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Standby)),
        ("pc-connected-bt-connected", new TrayVisualKey(HeadsetBaseVisual.PcConnected, BluetoothBadgeVisual.Connected)),
        ("dongle-only-headset-off", new TrayVisualKey(HeadsetBaseVisual.DongleOnlyHeadsetOff, BluetoothBadgeVisual.Off)),
        ("disconnected", new TrayVisualKey(HeadsetBaseVisual.Disconnected, BluetoothBadgeVisual.Off))
    ];

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    public static Bitmap RenderBitmap(TrayVisualKey key, int sizePx)
    {
        int size = Math.Clamp(sizePx, 16, 256);

        // Render on a 4x supersampled canvas and area-average downsample with HighQualityBilinear
        // (strict box/tent prefilter without bicubic overshoot lobes) so hollow cavities at 16x16
        // preserve pure Alpha == 0 centers while strokes stay silky-smooth on both #18181B and #F3F4F6.
        int workSize = Math.Max(128, size * 4);
        using var hiRes = new Bitmap(workSize, workSize, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(hiRes))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            float scale = workSize / 32f;
            g.ScaleTransform(scale, scale);

            GraphicsState baseState = g.Save();
            if (key.Badge != BluetoothBadgeVisual.Off)
            {
                using var badgeKnockout = new GraphicsPath();
                const float cx = 23.2f;
                const float cy = 23.2f;
                const float knockoutR = 8.2f;
                badgeKnockout.AddEllipse(cx - knockoutR, cy - knockoutR, knockoutR * 2f, knockoutR * 2f);
                g.SetClip(badgeKnockout, CombineMode.Exclude);
            }

            if (key.Base == HeadsetBaseVisual.Disconnected)
            {
                using GraphicsPath slashKnockout = CreateDisconnectedKnockoutPath(
                    includeCornerDot: key.Badge == BluetoothBadgeVisual.Off);
                g.SetClip(slashKnockout, CombineMode.Exclude);
            }

            DrawHeadsetBase(g, key.Base, size);
            g.Restore(baseState);

            if (key.Base == HeadsetBaseVisual.Disconnected)
            {
                DrawDisconnectedOverlay(g, hasBluetoothBadge: key.Badge != BluetoothBadgeVisual.Off);
            }

            if (key.Badge != BluetoothBadgeVisual.Off)
            {
                DrawBluetoothBadge(g, key.Badge, size);
            }
        }

        if (workSize == size)
        {
            return (Bitmap)hiRes.Clone();
        }

        var finalBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics gf = Graphics.FromImage(finalBmp))
        {
            gf.Clear(Color.Transparent);
            gf.SmoothingMode = SmoothingMode.AntiAlias;
            gf.InterpolationMode = InterpolationMode.HighQualityBilinear;
            gf.PixelOffsetMode = PixelOffsetMode.HighQuality;
            gf.CompositingQuality = CompositingQuality.HighQuality;
            gf.DrawImage(hiRes, new Rectangle(0, 0, size, size));
        }

        return finalBmp;
    }

    /// <summary>
    /// Creates a managed Icon that owns its underlying Win32 HICON handle and immediately
    /// destroys the temporary GetHicon() handle so zero GDI/USER handles leak.
    /// </summary>
    public static Icon CreateOwnedIcon(TrayVisualKey key, int sizePx)
    {
        using Bitmap bmp = RenderBitmap(key, sizePx);
        IntPtr rawHicon = bmp.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(rawHicon);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            if (rawHicon != IntPtr.Zero)
            {
                _ = DestroyIcon(rawHicon);
            }
        }
    }

    public static IReadOnlyList<string> ExportAllStates(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var writtenFiles = new List<string>(CanonicalExportStates.Length * StandardExportSizes.Length);
        foreach (var (name, key) in CanonicalExportStates)
        {
            foreach (int size in StandardExportSizes)
            {
                string filePath = Path.Combine(outputDirectory, $"{name}-{size}x{size}.png");
                using Bitmap bmp = RenderBitmap(key, size);
                bmp.Save(filePath, ImageFormat.Png);
                writtenFiles.Add(filePath);
            }
        }

        return writtenFiles;
    }

    private static void DrawHeadsetBase(Graphics g, HeadsetBaseVisual baseVisual, int sizePx)
    {
        var bandRect = new RectangleF(4.6f, 2.8f, 22.8f, 21.4f);
        var leftCup = new RectangleF(2.6f, 12.4f, 7.2f, 14.0f);
        var rightCup = new RectangleF(22.2f, 12.4f, 7.2f, 14.0f);
        const float cupRadius = 3.3f;

        switch (baseVisual)
        {
            case HeadsetBaseVisual.PcConnected:
            {
                Color deepGreenRim = Color.FromArgb(255, 20, 83, 45);    // #14532D
                Color vividGreen = Color.FromArgb(255, 34, 197, 94);     // #22C55E
                Color richGreen = Color.FromArgb(255, 22, 163, 74);      // #16A34A

                using (var bandOuterPen = new Pen(deepGreenRim, 4.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var bandInnerPen = new Pen(vividGreen, 2.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(bandOuterPen, bandRect, 180f, 180f);
                    g.DrawArc(bandInnerPen, bandRect, 180f, 180f);
                }

                if (sizePx >= 24)
                {
                    using var hiPen = new Pen(Color.FromArgb(190, 187, 247, 208), 1.0f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round
                    };
                    g.DrawArc(hiPen, new RectangleF(5.2f, 2.5f, 21.6f, 20.4f), 205f, 130f);
                }

                using GraphicsPath leftPath = CreateRoundedRect(leftCup, cupRadius);
                using GraphicsPath rightPath = CreateRoundedRect(rightCup, cupRadius);
                using var cupBrush = new LinearGradientBrush(
                    new PointF(0f, 12f),
                    new PointF(0f, 27f),
                    vividGreen,
                    richGreen);
                using var cupBorder = new Pen(deepGreenRim, 1.25f);

                g.FillPath(cupBrush, leftPath);
                g.FillPath(cupBrush, rightPath);
                g.DrawPath(cupBorder, leftPath);
                g.DrawPath(cupBorder, rightPath);
                break;
            }

            case HeadsetBaseVisual.DongleOnlyHeadsetOff:
            {
                // Generous hollow earcup geometry so BOTH 32x32 AND 16x16 (96 DPI taskbar) preserve
                // a multi-pixel pure Alpha == 0 transparent cavity on #18181B dark & #F3F4F6 light taskbars.
                Color darkAmberRim = Color.FromArgb(255, 146, 64, 14);   // #92400E (anchors on light surfaces)
                Color brightAmber = Color.FromArgb(255, 245, 158, 11);   // #F59E0B
                Color goldCore = Color.FromArgb(255, 251, 191, 36);      // #FBBF24 (pops on dark taskbar)

                var hollowBandRect = new RectangleF(4.4f, 2.6f, 23.2f, 21.2f);
                var offLeftCup = new RectangleF(1.6f, 11.6f, 8.8f, 15.0f);
                var offRightCup = new RectangleF(21.6f, 11.6f, 8.8f, 15.0f);

                float wall = sizePx <= 24 ? 1.55f : 1.75f;
                var leftInnerHole = new RectangleF(
                    offLeftCup.X + wall,
                    offLeftCup.Y + wall,
                    offLeftCup.Width - (wall * 2f),
                    offLeftCup.Height - (wall * 2f));
                var rightInnerHole = new RectangleF(
                    offRightCup.X + wall,
                    offRightCup.Y + wall,
                    offRightCup.Width - (wall * 2f),
                    offRightCup.Height - (wall * 2f));

                GraphicsState bandState = g.Save();
                using (GraphicsPath leftHoleClip = CreateRoundedRect(leftInnerHole, 1.4f))
                using (GraphicsPath rightHoleClip = CreateRoundedRect(rightInnerHole, 1.4f))
                {
                    g.SetClip(leftHoleClip, CombineMode.Exclude);
                    g.SetClip(rightHoleClip, CombineMode.Exclude);

                    using var bandOuterPen = new Pen(darkAmberRim, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    using var bandInnerPen = new Pen(goldCore, 2.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(bandOuterPen, hollowBandRect, 184f, 172f);
                    g.DrawArc(bandInnerPen, hollowBandRect, 184f, 172f);
                    g.Restore(bandState);
                }

                bool strokeInnerRim = sizePx > 24;
                DrawHollowCup(g, offLeftCup, leftInnerHole, 3.5f, 1.4f, darkAmberRim, brightAmber, goldCore, strokeInnerRim);
                DrawHollowCup(g, offRightCup, rightInnerHole, 3.5f, 1.4f, darkAmberRim, brightAmber, goldCore, strokeInnerRim);

                // Crisp amber USB receiver standby bar centered between earcups.
                using var standbyOuter = new Pen(darkAmberRim, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var standbyInner = new Pen(goldCore, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(standbyOuter, 13.6f, 25.4f, 18.4f, 25.4f);
                g.DrawLine(standbyInner, 13.6f, 25.4f, 18.4f, 25.4f);
                break;
            }

            default:
            {
                Color darkSlateRim = Color.FromArgb(255, 51, 65, 85);    // #334155
                Color midSlate = Color.FromArgb(255, 100, 116, 139);     // #64748B
                Color lightSlate = Color.FromArgb(255, 148, 163, 184);   // #94A3B8

                using (var bandOuterPen = new Pen(darkSlateRim, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var bandInnerPen = new Pen(lightSlate, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(bandOuterPen, bandRect, 180f, 180f);
                    g.DrawArc(bandInnerPen, bandRect, 180f, 180f);
                }

                using GraphicsPath leftPath = CreateRoundedRect(leftCup, cupRadius);
                using GraphicsPath rightPath = CreateRoundedRect(rightCup, cupRadius);
                using var cupBrush = new LinearGradientBrush(
                    new PointF(0f, 12f),
                    new PointF(0f, 27f),
                    lightSlate,
                    midSlate);
                using var cupBorder = new Pen(darkSlateRim, 1.3f);

                g.FillPath(cupBrush, leftPath);
                g.FillPath(cupBrush, rightPath);
                g.DrawPath(cupBorder, leftPath);
                g.DrawPath(cupBorder, rightPath);
                break;
            }
        }
    }

    private static void DrawHollowCup(
        Graphics g,
        RectangleF outerRect,
        RectangleF innerHole,
        float outerRadius,
        float innerRadius,
        Color rimColor,
        Color bodyColor,
        Color highlightColor,
        bool strokeInnerRim)
    {
        using var ringPath = new GraphicsPath(FillMode.Alternate);
        using (GraphicsPath outer = CreateRoundedRect(outerRect, outerRadius))
        using (GraphicsPath inner = CreateRoundedRect(innerHole, innerRadius))
        {
            ringPath.AddPath(outer, connect: false);
            ringPath.AddPath(inner, connect: false);

            using var bodyBrush = new LinearGradientBrush(
                new PointF(outerRect.X, outerRect.Top),
                new PointF(outerRect.X, outerRect.Bottom),
                highlightColor,
                bodyColor);
            using var rimPen = new Pen(rimColor, 0.8f) { LineJoin = LineJoin.Round };

            g.FillPath(bodyBrush, ringPath);
            g.DrawPath(rimPen, outer);
            if (strokeInnerRim)
            {
                g.DrawPath(rimPen, inner);
            }
        }
    }

    private static GraphicsPath CreateDisconnectedKnockoutPath(bool includeCornerDot)
    {
        var knockout = new GraphicsPath(FillMode.Winding);
        using (var slashPath = new GraphicsPath())
        {
            slashPath.AddLine(5.5f, 5.5f, 25.0f, 25.0f);
            using var widenPen = new Pen(Color.Black, 5.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            slashPath.Widen(widenPen);
            knockout.AddPath(slashPath, connect: false);
        }

        if (includeCornerDot)
        {
            const float cx = 24.6f;
            const float cy = 24.6f;
            const float r = 6.0f;
            knockout.AddEllipse(cx - r, cy - r, r * 2f, r * 2f);
        }

        return knockout;
    }

    private static void DrawDisconnectedOverlay(Graphics g, bool hasBluetoothBadge)
    {
        Color vividRed = Color.FromArgb(255, 239, 68, 68);  // #EF4444
        Color richRed = Color.FromArgb(255, 220, 38, 38);   // #DC2626
        Color deepRed = Color.FromArgb(255, 153, 27, 27);   // #991B1B

        using (var rimPen = new Pen(deepRed, 3.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var slashPen = new Pen(vividRed, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(rimPen, 5.5f, 5.5f, 25.0f, 25.0f);
            g.DrawLine(slashPen, 5.5f, 5.5f, 25.0f, 25.0f);
        }

        if (!hasBluetoothBadge)
        {
            const float cx = 24.6f;
            const float cy = 24.6f;
            const float r = 4.5f;
            var dotRect = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);

            using var redBrush = new LinearGradientBrush(dotRect, vividRed, richRed, LinearGradientMode.Vertical);
            using var rimPen = new Pen(deepRed, 0.9f);
            g.FillEllipse(redBrush, dotRect);
            g.DrawEllipse(rimPen, dotRect);
        }
    }

    private static void DrawBluetoothBadge(Graphics g, BluetoothBadgeVisual badge, int sizePx)
    {
        const float cx = 23.2f;
        const float cy = 23.2f;
        const float r = 6.8f;

        if (badge == BluetoothBadgeVisual.Connected)
        {
            var badgeRect = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
            using (var fillBrush = new LinearGradientBrush(
                badgeRect,
                Color.FromArgb(255, 59, 130, 246),  // #3B82F6
                Color.FromArgb(255, 37, 99, 235),   // #2563EB
                LinearGradientMode.Vertical))
            using (var rimPen = new Pen(Color.FromArgb(255, 29, 78, 216), 0.9f)) // #1D4ED8
            {
                g.FillEllipse(fillBrush, badgeRect);
                g.DrawEllipse(rimPen, badgeRect);
            }

            DrawBluetoothRune(g, cx, cy, Color.White, sizePx >= 24 ? 1.4f : 1.75f, simplified: sizePx < 24);
        }
        else if (badge == BluetoothBadgeVisual.Standby)
        {
            // High-visibility Standby ring:
            // - #93C5FD -> #3B82F6 fill (9.82:1 to 4.82:1 on #18181B dark taskbar)
            // - #1E40AF outer contour (7.93:1 on #F3F4F6 light taskbar) on outerRect ONLY
            // - Wide innerR = 4.25f so 16x16 preserves a clean Alpha == 0 hollow center.
            const float outerR = 6.8f;
            const float innerR = 4.25f;
            var outerRect = new RectangleF(cx - outerR, cy - outerR, outerR * 2f, outerR * 2f);
            var innerRect = new RectangleF(cx - innerR, cy - innerR, innerR * 2f, innerR * 2f);

            using var ringPath = new GraphicsPath(FillMode.Alternate);
            ringPath.AddEllipse(outerRect);
            ringPath.AddEllipse(innerRect);

            using var ringBrush = new LinearGradientBrush(
                outerRect,
                Color.FromArgb(255, 147, 197, 253), // #93C5FD
                Color.FromArgb(255, 59, 130, 246),  // #3B82F6
                LinearGradientMode.Vertical);
            using var outerContourPen = new Pen(Color.FromArgb(255, 30, 64, 175), 0.75f); // #1E40AF

            g.FillPath(ringBrush, ringPath);
            g.DrawEllipse(outerContourPen, outerRect);
        }
    }

    private static void DrawBluetoothRune(
        Graphics g,
        float cx,
        float cy,
        Color color,
        float strokeWidth,
        bool simplified)
    {
        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        if (simplified)
        {
            g.DrawLine(pen, cx, cy - 4.4f, cx, cy + 4.4f);
            g.DrawLines(pen,
            [
                new PointF(cx - 2.4f, cy - 2.2f),
                new PointF(cx + 2.5f, cy + 2.0f),
                new PointF(cx, cy + 4.3f)
            ]);
            g.DrawLines(pen,
            [
                new PointF(cx - 2.4f, cy + 2.2f),
                new PointF(cx + 2.5f, cy - 2.0f),
                new PointF(cx, cy - 4.3f)
            ]);
            return;
        }

        PointF top = new(cx - 0.3f, cy - 4.8f);
        PointF bottom = new(cx - 0.3f, cy + 4.8f);
        PointF upperRight = new(cx + 2.9f, cy - 2.3f);
        PointF lowerRight = new(cx + 2.9f, cy + 2.3f);
        PointF upperLeft = new(cx - 2.9f, cy - 2.2f);
        PointF lowerLeft = new(cx - 2.9f, cy + 2.2f);

        g.DrawLine(pen, top, bottom);
        g.DrawLines(pen, [lowerLeft, upperRight, top]);
        g.DrawLines(pen, [upperLeft, lowerRight, bottom]);
    }

    private static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
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
