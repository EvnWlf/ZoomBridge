using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ZoomBridge.UI
{
    public static class IconHelper
    {
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;

        /// <summary>
        /// Obtiene un icono nativo ultra nítido ajustado exactamente al tamaño en píxeles que Windows
        /// requiere para el System Tray según el DPI actual (16, 20, 24, 32, etc.),
        /// eliminando cualquier borrosidad o escalado defectuoso.
        /// </summary>
        public static Icon GetCrispTrayIcon()
        {
            int size = GetSystemMetrics(SM_CXSMICON);
            if (size <= 0) size = 24;

            // 1. Intentar cargar el frame exacto del recurso ICO multi-resolución
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/ZoomBridge.ico", UriKind.Absolute);
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using var s = streamInfo.Stream;
                    return new Icon(s, size, size);
                }
            }
            catch
            {
                // Continuar a fallback
            }

            // 2. Intentar cargar desde el archivo físico si está presente
            string localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ZoomBridge.ico");
            if (File.Exists(localPath))
            {
                try
                {
                    using var fs = File.OpenRead(localPath);
                    return new Icon(fs, size, size);
                }
                catch { }
            }

            // 3. Fallback: renderizado dinámico con super-muestreo 4x y antialiasing bicúbico
            using var bmp = RenderIconBitmap(size);
            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        /// <summary>
        /// Genera una imagen BitmapSource nítida en alta resolución (256x256)
        /// para encabezados de ventanas y flyouts con renderizado de máxima calidad.
        /// </summary>
        public static BitmapSource GetHighResImageSource()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/ZoomBridge.png", UriKind.Absolute);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = uri;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                // Fallback a render dinámico
                using var bmp = RenderIconBitmap(256);
                var hBitmap = bmp.GetHbitmap();
                try
                {
                    var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Dibuja el icono con super-muestreo (4x) y filtro bicúbico para bordes suaves y nítidos.
        /// </summary>
        public static Bitmap RenderIconBitmap(int targetSize)
        {
            int s = targetSize * 4; // 4x supersampling
            using var bigBmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bigBmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float margin = (float)Math.Max(1.0, s * 0.05);
                float width = s - (2 * margin);
                float height = s - (2 * margin);
                float radius = s * 0.22f;

                // Tarjeta de fondo redondeada con gradiente Fluent Windows 11
                using (var path = CreateRoundRect(margin, margin, width, height, radius))
                {
                    var rect = new RectangleF(0, 0, s, s);
                    using (var brush = new LinearGradientBrush(
                        rect,
                        Color.FromArgb(255, 14, 136, 250),
                        Color.FromArgb(255, 0, 84, 215),
                        60f))
                    {
                        g.FillPath(brush, path);
                    }

                    // Borde sutil interior de luz Fluent
                    using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1, s * 0.02f)))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                // Símbolo de cámara Fluent moderno en blanco puro
                using (var white = new SolidBrush(Color.White))
                {
                    float bodyX = s * 0.20f;
                    float bodyY = s * 0.31f;
                    float bodyW = s * 0.42f;
                    float bodyH = s * 0.38f;
                    float bodyR = s * 0.09f;

                    using (var bodyPath = CreateRoundRect(bodyX, bodyY, bodyW, bodyH, bodyR))
                    {
                        g.FillPath(white, bodyPath);
                    }

                    // Lente de la cámara con proporciones Fluent
                    var lensPoints = new PointF[]
                    {
                        new PointF(s * 0.64f, s * 0.43f),
                        new PointF(s * 0.81f, s * 0.32f),
                        new PointF(s * 0.81f, s * 0.68f),
                        new PointF(s * 0.64f, s * 0.57f)
                    };
                    g.FillPolygon(white, lensPoints);
                }
            }

            // Redimensionar al tamaño destino con interpolación bicúbica de alta calidad
            var finalBmp = new Bitmap(targetSize, targetSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var gFinal = Graphics.FromImage(finalBmp))
            {
                gFinal.SmoothingMode = SmoothingMode.HighQuality;
                gFinal.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gFinal.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gFinal.Clear(Color.Transparent);
                gFinal.DrawImage(bigBmp, new Rectangle(0, 0, targetSize, targetSize));
            }

            return finalBmp;
        }

        private static GraphicsPath CreateRoundRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
