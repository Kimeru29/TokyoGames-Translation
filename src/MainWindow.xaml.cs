using System.Drawing;
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
using Newtonsoft.Json;
using Tesseract;
using System.Windows.Input;
using System.Windows.Controls;


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
    public string Id { get; set; }
    public string Object { get; set; }
    public long Created { get; set; }
    public string Model { get; set; }
    public Usage Usage { get; set; }
    public Choice[] Choices { get; set; }
}

public class Usage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

public class Choice
{
    public Message Message { get; set; }
}

public class Message
{
    public string Role { get; set; }
    public string Content { get; set; }
}



public partial class MainWindow : Window
{
    static readonly HttpClient client = new HttpClient();
    private DispatcherTimer screenshotTimer;
    private System.Windows.Point _startPoint;
    private System.Windows.Rect _selectionRectangle;
    private bool _isMouseDown;
    private System.Windows.Rect _captureRect;
    private string lastText = string.Empty;
    private int translationCount = 0;
    private bool canReplace = true;
    private int actionButtonPressCount = 0;
    private int actionButtonReleaseCount = 0;
    private DispatcherTimer timer = new DispatcherTimer();
    private bool screenshotTaken = false;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += new RoutedEventHandler(MainWindow_Loaded);

        this.Loaded += (sender, e) =>
        {
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        };

        this.Width = SystemParameters.PrimaryScreenWidth;
        this.Height = SystemParameters.PrimaryScreenHeight;

        this.Left = SystemParameters.PrimaryScreenWidth * 0.25;

        InitializeScreenshotTimer();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        this.Width = screenWidth / 2;   // Half the screen width
        this.Height = screenHeight;     // Full screen height
        this.Left = 0;                  // Align to the left
        this.Top = 0;                   // Align to the top
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K)
        {
            actionButtonPressCount++;
        }
    }


    #region Events
    #region Screenshot events
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (screenshotTaken) return;

        _startPoint = e.GetPosition(this);
        _isMouseDown = true;
        MyCanvas.CaptureMouse();
        selectionRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(selectionRectangle, _startPoint.X);
        Canvas.SetTop(selectionRectangle, _startPoint.Y);
        selectionRectangle.Width = 0;
        selectionRectangle.Height = 0;
    }


    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (screenshotTaken) return;

        if (_isMouseDown)
        {
            var currentPoint = e.GetPosition(this);
            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(_startPoint.X - currentPoint.X);
            var height = Math.Abs(_startPoint.Y - currentPoint.Y);

            Canvas.SetLeft(selectionRectangle, x);
            Canvas.SetTop(selectionRectangle, y);
            selectionRectangle.Width = width;
            selectionRectangle.Height = height;

            _selectionRectangle = new System.Windows.Rect(
    _selectionRectangle.Left,
    _selectionRectangle.Top,
    _selectionRectangle.Width,
    _selectionRectangle.Height);
        }
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (screenshotTaken) return;

        if (_isMouseDown)
        {
            _isMouseDown = false;

            System.Windows.Point screenTopLeft = this.PointToScreen(new System.Windows.Point(Canvas.GetLeft(selectionRectangle), Canvas.GetTop(selectionRectangle)));

            _captureRect = new System.Windows.Rect(
    screenTopLeft.X,
    screenTopLeft.Y,
    selectionRectangle.Width,
    selectionRectangle.Height);

            CaptureScreenArea("screenshot.png");

            selectionRectangle.Visibility = Visibility.Collapsed;

            if (sender is Canvas canvas)
            {
                canvas.ReleaseMouseCapture();
            }

            screenshotTaken = true;
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
        screenshotTimer.Interval = TimeSpan.FromSeconds(15);
        screenshotTimer.Tick += ScreenshotTimer_Tick;
        screenshotTimer.Start();
    }

    private async void ScreenshotTimer_Tick(object sender, EventArgs e)
    {
        if(actionButtonPressCount == actionButtonReleaseCount) 
            return;

        actionButtonReleaseCount++;

        var bitmapImage = CaptureScreenArea("");
        var text = await PerformOcrTess(@"C:\test\image.png", bitmapImage);

        int levenshteinDistance = ComputeLevenshteinDistance(text, lastText);
        double similarity = 1.0 - (double)levenshteinDistance / Math.Max(text.Length, lastText.Length);

        string filePath = @"C:\test\file.txt";

        if (similarity <= 0.5 || translationCount == 0)
        {
            translationCount++;            
            text += $"{text} {await TranslateJapaneseToEnglish(text)}\n\n";    

            File.AppendAllText(filePath, text);
            lastText = text;
            canReplace = true;
        }
        else if (similarity > 0.5 && canReplace)
        {
            canReplace = false;
            var lines = File.ReadAllLines(filePath).ToList();
            if (lines.Count > 0)
            {
                lines[lines.Count - 1] = text;
                File.WriteAllLines(filePath, lines);
            }
            else
            {
                File.AppendAllText(filePath, text + "\n");
            }
        }
    }

    #endregion

    #region Helper methods
    public static int ComputeLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        int sourceLength = source.Length;
        int targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        // Initialize the distance matrix
        for (int i = 0; i <= sourceLength; distance[i, 0] = i++) { }
        for (int j = 0; j <= targetLength; distance[0, j] = j++) { }

        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }


    #endregion



    #region OCR/Translation methods
    public async Task<string> PerformOcrTess(string imagePath, Bitmap image)
    {
        string tessdataPath = @"C:\tessdata"; string language = "jpn";
        byte[] imageData = BitmapToByteArray(image);
        image.Dispose();

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
        if (string.IsNullOrEmpty(japaneseText)) return "";
        var apiKey = "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"; // Your API key
        string prompt = $"You're a professional Japanese-to-English game translator. Translate and adapt for American audiences the following japanese text (this text can be minimal, don't worry about that no need to warm me), provide an accurate translation but also a translation that makes sense in english but retains the original idea DON'T explain your translation: \n'{japaneseText}'";

        var requestData = new
        {
            model = "gpt-4-1106-preview",
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.7
        };

        string json = JsonConvert.SerializeObject(requestData);
        StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();

        var openAIResponse = JsonConvert.DeserializeObject<OpenAIResponse>(responseBody);

        // Extracting only the content of the response
        string messageContent = openAIResponse?.Choices[0]?.Message?.Content.Trim();
        return messageContent;
    }

    #endregion

    #region legacy methods
    private async Task<string> PerformOcr(Bitmap image)
    {
        IronOcr.License.LicenseKey = "xxxxx";
        var ocr = new IronTesseract();

        string result;

        using (var ocrInput = new OcrInput())
        {
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
        IntPtr notepadHandle = FindWindow(); if (notepadHandle == IntPtr.Zero)
            throw new InvalidOperationException("Game window not found.");

        NativeMethods.GetClientRect(notepadHandle, out var rect);

        NativeMethods.POINT topLeft = new NativeMethods.POINT(rect.Left, rect.Top);
        NativeMethods.ClientToScreen(notepadHandle, ref topLeft);

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        Bitmap bmp = new Bitmap(width, height);

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