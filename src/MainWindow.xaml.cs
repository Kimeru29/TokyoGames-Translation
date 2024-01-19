using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IronOcr;


namespace TokyoGames_Translation;

public class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}

public partial class MainWindow : Window
{
    private DispatcherTimer screenshotTimer;

    public MainWindow()
    {
        InitializeComponent();

        this.Width = SystemParameters.PrimaryScreenWidth;
        this.Height = SystemParameters.PrimaryScreenHeight;

        this.Left = SystemParameters.PrimaryScreenWidth * 0.25;

        InitializeScreenshotTimer();
    }

    public static Bitmap CaptureWindow()
    {
        IntPtr notepadHandle = FindWindow();
        if (notepadHandle == IntPtr.Zero)
            throw new InvalidOperationException("Notepad window not found.");

                NativeMethods.GetWindowRect(notepadHandle, out var rect);

                Bitmap bmp = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);

                using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bmp.Size);
        }

        return bmp;
    }

    private static IntPtr FindWindow()
    {
        IntPtr foundWindow = IntPtr.Zero;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            StringBuilder windowText = new StringBuilder(255);
            NativeMethods.GetWindowText(hWnd, windowText, 255);

            if (windowText.ToString().Contains("Edge"))
            {
                foundWindow = hWnd;
                return false;              }

            return true;          }, IntPtr.Zero);

        return foundWindow;
    }



    public static Bitmap CaptureSecondHalfOfWindow(Window window)
    {
        if (window == null)
            throw new ArgumentNullException(nameof(window));

        window.UpdateLayout();

        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new VisualBrush(window);
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            context.DrawRectangle(visual, null, new Rect(new System.Windows.Point(), new System.Windows.Size(width, height)));
        }

        renderTarget.Render(drawingVisual);
        
        var crop = new CroppedBitmap(renderTarget, new Int32Rect(width / 2, 0, width / 2, height));

        var screenshootBitmap = CaptureWindow();
        SaveBitmapToFile(screenshootBitmap, @"C:\test\image.png");

        return screenshootBitmap;
    }

    private static Bitmap ConvertBitmapSourceToBitmap(BitmapSource bitmapSource)
    {
        using (MemoryStream outStream = new MemoryStream())
        {
            BitmapEncoder enc = new BmpBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bitmapSource));
            enc.Save(outStream);
            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(outStream);

            return new Bitmap(bitmap);
        }
    }

    public static void SaveBitmapToFile(Bitmap bitmap, string filePath)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        bitmap.Save(filePath, ImageFormat.Png);
    }

    private void InitializeScreenshotTimer()
    {
        screenshotTimer = new DispatcherTimer();
        screenshotTimer.Interval = TimeSpan.FromSeconds(30);
        screenshotTimer.Tick += ScreenshotTimer_Tick;
        screenshotTimer.Start();
    }

    private void ScreenshotTimer_Tick(object sender, EventArgs e)
    {
        var bitmapImage = CaptureSecondHalfOfWindow(this);
        var text = PerformOcr(bitmapImage);
        Console.WriteLine(text);
    }

    private string PerformOcr(Bitmap image)
    {
        IronOcr.License.LicenseKey = "IRONSUITE.DIGNIHAYDE.GUFUM.COM.753-F04DDFB2D0-FFRJHTDAVQLJH4-ABZXRIQIOVEX-OYSG3DGNBV7L-PDVUJPNRKK4U-MA5FMTKYLFIU-AHCIFLVZRAI2-XREMGA-TYP2BYPNVS6LUA-DEPLOYMENT.TRIAL-E2PDY3.TRIAL.EXPIRES.18.FEB.2024";
        var ocr = new IronTesseract();

        string result;

        using (var ocrInput = new OcrInput())
        {
            ocrInput.AddImage(image);


            ocr.Language = OcrLanguage.Japanese;

            var ocrResult = ocr.Read(ocrInput);
            result = ocrResult.Text;
        }

        return result;
    }

}