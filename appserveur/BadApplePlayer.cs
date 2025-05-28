using System.Drawing;
using System.Text;
using System.Threading;


namespace ServerMachin
{
    public class BadApplePlayer
    {
        private Thread? _thread;
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        private CancellationTokenSource? _cts;
        private readonly Action<string> _broadcast;

        public BadApplePlayer(Action<string> broadcast)
        {
            _broadcast = broadcast;
        }

        public void Start()
        {
            if (_thread != null && _thread.IsAlive)
            {
                Console.WriteLine("Bad Apple animation already on");
                return;
            }
            _cts = new CancellationTokenSource();
            _pauseEvent.Set();
            _thread = new Thread(() => PlayBadAppleFromImages(_cts.Token));
            _thread.Start();
            Console.WriteLine("Bad Apple animation launched.");
        }

        public void Pause()
        {
            _pauseEvent.Reset();
            Console.WriteLine("Bad Apple animation paused.");
        }

        public void Resume()
        {
            _pauseEvent.Set();
            Console.WriteLine("Bad Apple animation resumed.");
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _thread = null;
                Console.WriteLine("Bad Apple animation stopped.");
            }
        }

        private void PlayBadAppleFromImages(CancellationToken token)
        {
            try
            {
                string framesDir = "frames";
                if (!Directory.Exists(framesDir))
                {
                    Console.WriteLine("frames folder hasn't been found");
                    return;
                }

                var files = Directory.GetFiles(framesDir)
                    .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    Console.WriteLine("No images found in frames folder");
                    return;
                }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    _pauseEvent.Wait(token);
                    string ascii = ImageToAscii(file, 80, 30);
                    _broadcast("\x1b[2J\x1b[H" + ascii);
                    Thread.Sleep(33);
                }

                Console.WriteLine("Bad Apple Animation broadcasted to every client.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Bad Apple animation correctly stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while Bad Apple animation : {ex.Message}");
            }
            finally
            {
                _thread = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private static string ImageToAscii(string imagePath, int width, int height)
        {
            const string asciiChars = "@%#*+=-:. ";
            using (var bmp = new Bitmap(imagePath))
            using (var resized = new Bitmap(bmp, new Size(width, height)))
            {
                StringBuilder sb = new StringBuilder();
                for (int y = 0; y < resized.Height; y++)
                {
                    for (int x = 0; x < resized.Width; x++)
                    {
                        Color pixel = resized.GetPixel(x, y);
                        int gray = (pixel.R + pixel.G + pixel.B) / 3;
                        int idx = gray * (asciiChars.Length - 1) / 255;
                        sb.Append(asciiChars[idx]);
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }
    }
}