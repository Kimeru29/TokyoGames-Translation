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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tesseract;
using System.Windows.Input;
using System.Windows.Controls;
using static System.Net.Mime.MediaTypeNames;


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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            this.X = x;
            this.Y = y;
        }
    }
}


public class OpenAIResponse
{
    [JsonProperty("choices")]
    public List<Choice> Choices { get; set; }

    public class Choice
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}


public partial class MainWindow : Window
{
    static readonly HttpClient client = new HttpClient();
    private DispatcherTimer screenshotTimer;
    private System.Windows.Point _startPoint; // WPF Point for capturing start position
    private System.Windows.Rect _selectionRectangle; // Drawing Rectangle to define the capture area
    private bool _isMouseDown; // Flag to track if the mouse button is pressed

    private System.Windows.Rect _captureRect; // Store the coordinates for the screenshot area


    public MainWindow()
    {
        InitializeComponent();

        this.Loaded += (sender, e) =>
        {
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)); // Nearly transparent
        };

        this.Width = SystemParameters.PrimaryScreenWidth;
        this.Height = SystemParameters.PrimaryScreenHeight;

        this.Left = SystemParameters.PrimaryScreenWidth * 0.25;

        InitializeScreenshotTimer();
    }

    #region Events
    #region Screenshot events
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _isMouseDown = true;
        MyCanvas.CaptureMouse(); // Use the named Canvas directly

        // Initialize the selection rectangle here
        selectionRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(selectionRectangle, _startPoint.X);
        Canvas.SetTop(selectionRectangle, _startPoint.Y);
        selectionRectangle.Width = 0;
        selectionRectangle.Height = 0;
    }


    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isMouseDown)
        {
            var currentPoint = e.GetPosition(this);
            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(_startPoint.X - currentPoint.X);
            var height = Math.Abs(_startPoint.Y - currentPoint.Y);

            // Update the visual representation
            Canvas.SetLeft(selectionRectangle, x);
            Canvas.SetTop(selectionRectangle, y);
            selectionRectangle.Width = width;
            selectionRectangle.Height = height;

            // Update the global variable for the screenshot
            _selectionRectangle = new System.Windows.Rect(
                _selectionRectangle.Left,
                _selectionRectangle.Top,
                _selectionRectangle.Width,
                _selectionRectangle.Height);
        }
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMouseDown)
        {
            _isMouseDown = false;

            // Convert the canvas-relative coordinates to screen coordinates
            System.Windows.Point screenTopLeft = this.PointToScreen(new System.Windows.Point(Canvas.GetLeft(selectionRectangle), Canvas.GetTop(selectionRectangle)));

            // Create a Rect with the size of the selection and the screen-relative coordinates
            _captureRect = new System.Windows.Rect(
                screenTopLeft.X,
                screenTopLeft.Y,
                selectionRectangle.Width,
                selectionRectangle.Height);

            // Now call the capture method with the screen coordinates
            CaptureScreenArea("screenshot.png");

            // Hide the selection rectangle
            selectionRectangle.Visibility = Visibility.Collapsed;

            // Release the mouse capture
            if (sender is Canvas canvas)
            {
                canvas.ReleaseMouseCapture();
            }
        }
    }
    #endregion
    #endregion

    #region Screenshot methods

    public Bitmap CaptureScreenArea(string filePath)
    {

        selectionRectangle.Visibility = Visibility.Collapsed;

        Bitmap bmp = new Bitmap((int)_captureRect.Width, (int)_captureRect.Height);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(new System.Drawing.Point((int)_captureRect.X, (int)_captureRect.Y), System.Drawing.Point.Empty, new System.Drawing.Size((int)_captureRect.Width, (int)_captureRect.Height));
        }
        bmp.Save(@"C:\\test\1.png", System.Drawing.Imaging.ImageFormat.Png);

        return bmp;

    }

    private void InitializeScreenshotTimer()
    {
        screenshotTimer = new DispatcherTimer();
        screenshotTimer.Interval = TimeSpan.FromSeconds(10);
        screenshotTimer.Tick += ScreenshotTimer_Tick;
        screenshotTimer.Start();
    }

    private async void ScreenshotTimer_Tick(object sender, EventArgs e)
    {
        var bitmapImage = CaptureScreenArea("");
        var text = await PerformOcrTess(@"C:\test\image.png", bitmapImage);
        var translation = await TranslateJapaneseToEnglish(text);
    }
    #endregion



    #region OCR/Translation methods
    public async Task<string> PerformOcrTess(string imagePath, Bitmap image)
    {
        string tessdataPath = @"C:\tessdata"; // Set the path to the tessdata directory
        string language = "jpn"; // Set the language to Japanese

        // Convert Bitmap to byte array
        byte[] imageData = BitmapToByteArray(image);
        image.Dispose();

        // Perform OCR using Tesseract
        using (var engine = new TesseractEngine(tessdataPath, language, EngineMode.LstmOnly))
        {
            using (var pix = Pix.LoadTiffFromMemory(imageData))
            {
                using (var page = engine.Process(pix))
                {
                    string text = page.GetText();
                    return text;
                }
            }
        }
    }
    private byte[] BitmapToByteArray(Bitmap image)
    {
        using (var ms = new MemoryStream())
        {            
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Tiff);
            return ms.ToArray();
        }
    }


    public async Task<string> TranslateJapaneseToEnglish(string japaneseText)
    {
        var apiKey = "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"; // Replace with your API key
        var url = "https://api.openai.com/v1/engines/gpt-4/completions"; // URL for GPT-4 API

        var requestData = new
        {
            prompt = "Translate this Japanese text to English: '" + japaneseText + "'",
            temperature = 0.7,
            max_tokens = 60
        };

        var json = JsonConvert.SerializeObject(requestData);
        var data = new StringContent(json, Encoding.UTF8, "application/json");

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsync(url, data);
        string result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("API request failed: " + result);
        }

        dynamic jsonResponse = JsonConvert.DeserializeObject(result);
        return jsonResponse.choices[0].text;
    }
    #endregion

    #region legacy methods
    //TODO: Fix japanese text recognition. Currently it is not working.
    private async Task<string> PerformOcr(Bitmap image)
    {
        IronOcr.License.LicenseKey = "xxxxx";
        var ocr = new IronTesseract();

        string result;

        using (var ocrInput = new OcrInput())
        {
            //ocrInput.AddImage(image);
            ocrInput.AddImage(@"C:\test\image.png");

            ocr.Language = OcrLanguage.JapaneseAlphabetBest;

            var ocrResult = ocr.Read(ocrInput);
            result = ocrResult.Text;

            var translation = await TranslateJapaneseToEnglish(result);
            var h = 8;
        }

        return result;
    }

    public static Bitmap CaptureWindow()
    {
        IntPtr notepadHandle = FindWindow(); // Make sure this function is correctly defined to find your window
        if (notepadHandle == IntPtr.Zero)
            throw new InvalidOperationException("Game window not found.");

        // Get the coordinates of the window's client area
        NativeMethods.GetClientRect(notepadHandle, out var rect);

        // Convert client coordinates to screen coordinates
        NativeMethods.POINT topLeft = new NativeMethods.POINT(rect.Left, rect.Top);
        NativeMethods.ClientToScreen(notepadHandle, ref topLeft);

        // Calculate width and height
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        // Create the bitmap with the correct dimensions
        Bitmap bmp = new Bitmap(width, height);

        // Use Graphics to get the image
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(topLeft.X, topLeft.Y, 0, 0, new System.Drawing.Size(width, height));
        }

        SaveBitmapToFile(bmp, @"C:\test\image.png");
        return bmp;
    }

    private static IntPtr FindWindow()
    {
        IntPtr foundWindow = IntPtr.Zero;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            StringBuilder windowText = new StringBuilder(255);
            NativeMethods.GetWindowText(hWnd, windowText, 255);

            if (windowText.ToString().Contains("Tales of the World - Narikiri Dungeon 2 (Japan) - VisualBoyAdvance-M 2.1.8"))
            {
                foundWindow = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

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
            context.DrawRectangle(visual, null, new System.Windows.Rect(new System.Windows.Point(), new System.Windows.Size(width, height)));
        }

        renderTarget.Render(drawingVisual);

        var crop = new CroppedBitmap(renderTarget, new Int32Rect(width / 2, 0, width / 2, height));

        var screenshootBitmap = CaptureWindow();

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

        bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

    }

    #endregion

}