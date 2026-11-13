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
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        float scale = size / 32f;
        g.ScaleTransform(scale, scale);

        DrawHeadsetBase(g, key.Base, size);

        if (key.Base == HeadsetBaseVisual.Disconnected)
        {
            DrawDisconnectedOverlay(g, hasBluetoothBadge: key.Badge != BluetoothBadgeVisual.Off);
        }

        if (key.Badge != BluetoothBadgeVisual.Off)
        {
            DrawBluetoothBadge(g, key.Badge, size);
        }

        return bmp;
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
        var leftCup = new RectangleF(2.8f, 12.6f, 6.8f, 13.6f);
        var rightCup = new RectangleF(22.4f, 12.6f, 6.8f, 13.6f);
        const float cupRadius = 3.1f;

        switch (baseVisual)
        {
            case HeadsetBaseVisual.PcConnected:
            {
                Color deepGreenRim = Color.FromArgb(255, 21, 128, 61);  // #15803D
                Color vividGreen = Color.FromArgb(255, 34, 197, 94);    // #22C55E
                Color richGreen = Color.FromArgb(255, 22, 163, 74);     // #16A34A

                using (var bandOuterPen = new Pen(deepGreenRim, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var bandInnerPen = new Pen(vividGreen, 2.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(bandOuterPen, bandRect, 180f, 180f);
                    g.DrawArc(bandInnerPen, bandRect, 180f, 180f);
                }

                if (sizePx >= 24)
                {
                    using var hiPen = new Pen(Color.FromArgb(180, 187, 247, 208), 1.0f)
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
                using var cupBorder = new Pen(deepGreenRim, 1.35f);

                g.FillPath(cupBrush, leftPath);
                g.FillPath(cupBrush, rightPath);
                g.DrawPath(cupBorder, leftPath);
                g.DrawPath(cupBorder, rightPath);
                break;
            }

            case HeadsetBaseVisual.DongleOnlyHeadsetOff:
            {
                Color deepAmberRim = Color.FromArgb(255, 180, 83, 9);     // #B45309
                Color richAmber = Color.FromArgb(255, 217, 119, 6);       // #D97706
                Color brightAmber = Color.FromArgb(255, 245, 158, 11);    // #F59E0B
                Color warmLightHollow = Color.FromArgb(255, 255, 251, 235); // #FFFBEB

                using (var bandOuterPen = new Pen(deepAmberRim, 4.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var bandInnerPen = new Pen(brightAmber, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(bandOuterPen, bandRect, 180f, 180f);
                    g.DrawArc(bandInnerPen, bandRect, 180f, 180f);
                }

                using GraphicsPath leftPath = CreateRoundedRect(leftCup, cupRadius);
                using GraphicsPath rightPath = CreateRoundedRect(rightCup, cupRadius);
                using var lightInside = new SolidBrush(warmLightHollow);
                using var amberOutline = new Pen(richAmber, 2.2f);

                g.FillPath(lightInside, leftPath);
                g.FillPath(lightInside, rightPath);
                g.DrawPath(amberOutline, leftPath);
                g.DrawPath(amberOutline, rightPath);

                // Crisp amber USB/standby bar centered near bottom between earcups.
                using var standbyOuter = new Pen(deepAmberRim, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var standbyInner = new Pen(brightAmber, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(standbyOuter, 13.0f, 25.5f, 19.0f, 25.5f);
                g.DrawLine(standbyInner, 13.0f, 25.5f, 19.0f, 25.5f);
                break;
            }

            default:
            {
                Color slateRim = Color.FromArgb(255, 51, 65, 85);       // #334155
                Color slateBand = Color.FromArgb(255, 71, 85, 105);     // #475569
                Color slateCupFill = Color.FromArgb(255, 100, 116, 139); // #64748B

                using (var bandOuterPen = new Pen(slateRim, 4.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var bandInnerPen = new Pen(slateCupFill, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(bandOuterPen, bandRect, 180f, 180f);
                    g.DrawArc(bandInnerPen, bandRect, 180f, 180f);
                }

                using GraphicsPath leftPath = CreateRoundedRect(leftCup, cupRadius);
                using GraphicsPath rightPath = CreateRoundedRect(rightCup, cupRadius);
                using var cupBrush = new SolidBrush(slateBand);
                using var cupBorder = new Pen(slateRim, 1.3f);

                g.FillPath(cupBrush, leftPath);
                g.FillPath(cupBrush, rightPath);
                g.DrawPath(cupBorder, leftPath);
                g.DrawPath(cupBorder, rightPath);
                break;
            }
        }
    }

    private static void DrawDisconnectedOverlay(Graphics g, bool hasBluetoothBadge)
    {
        Color whiteMoat = Color.FromArgb(250, 255, 255, 255);
        Color red = Color.FromArgb(255, 220, 38, 38);       // #DC2626
        Color deepRed = Color.FromArgb(255, 153, 27, 27);   // #991B1B

        // Bold diagonal disconnect slash with crisp white separator moat.
        using (var moatPen = new Pen(whiteMoat, 5.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var slashPen = new Pen(red, 3.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(moatPen, 5.5f, 5.5f, 25.2f, 25.2f);
            g.DrawLine(slashPen, 5.5f, 5.5f, 25.2f, 25.2f);
        }

        if (!hasBluetoothBadge)
        {
            const float cx = 24.6f;
            const float cy = 24.6f;
            const float r = 4.5f;

            using var moatBrush = new SolidBrush(whiteMoat);
            using var redBrush = new SolidBrush(red);
            using var rimPen = new Pen(deepRed, 1.0f);
            g.FillEllipse(moatBrush, cx - r - 1.4f, cy - r - 1.4f, (r + 1.4f) * 2f, (r + 1.4f) * 2f);
            g.FillEllipse(redBrush, cx - r, cy - r, r * 2f, r * 2f);
            g.DrawEllipse(rimPen, cx - r, cy - r, r * 2f, r * 2f);
        }
    }

    private static void DrawBluetoothBadge(Graphics g, BluetoothBadgeVisual badge, int sizePx)
    {
        const float cx = 23.2f;
        const float cy = 23.2f;
        const float r = 6.8f;

        Color whiteMoat = Color.FromArgb(250, 255, 255, 255);
        using (var moatBrush = new SolidBrush(whiteMoat))
        {
            g.FillEllipse(moatBrush, cx - r - 1.4f, cy - r - 1.4f, (r + 1.4f) * 2f, (r + 1.4f) * 2f);
        }

        if (badge == BluetoothBadgeVisual.Connected)
        {
            var badgeRect = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
            using (var fillBrush = new LinearGradientBrush(
                badgeRect,
                Color.FromArgb(255, 37, 99, 235),  // #2563EB
                Color.FromArgb(255, 29, 78, 216),  // #1D4ED8
                LinearGradientMode.Vertical))
            using (var rimPen = new Pen(Color.FromArgb(255, 30, 64, 175), 1.0f)) // #1E40AF
            {
                g.FillEllipse(fillBrush, badgeRect);
                g.DrawEllipse(rimPen, badgeRect);
            }

            DrawBluetoothRune(g, cx, cy, Color.White, sizePx >= 24 ? 1.35f : 1.7f, simplified: sizePx < 24);
        }
        else if (badge == BluetoothBadgeVisual.Standby)
        {
            // Light #EFF6FF hollow center with crisp #2563EB ring so Standby is unmistakable on light surfaces.
            const float ringRadius = 5.7f;
            using (var hollowFill = new SolidBrush(Color.FromArgb(255, 239, 246, 255))) // #EFF6FF
            using (var ringPen = new Pen(Color.FromArgb(255, 37, 99, 235), 2.1f))       // #2563EB
            {
                g.FillEllipse(hollowFill, cx - r, cy - r, r * 2f, r * 2f);
                g.DrawEllipse(ringPen, cx - ringRadius, cy - ringRadius, ringRadius * 2f, ringRadius * 2f);
            }

            if (sizePx >= 48)
            {
                DrawBluetoothRune(g, cx, cy, Color.FromArgb(150, 37, 99, 235), 1.2f, simplified: false);
            }
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
