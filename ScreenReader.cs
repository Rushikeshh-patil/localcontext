using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace LocalContextBuilder
{
    public static class ScreenReader
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static async Task<string> GetActiveWindowTextAsync()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return "";

                if (GetWindowRect(hWnd, out RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    if (width <= 0 || height <= 0) return "";

                    using (Bitmap bmp = new Bitmap(width, height))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
                        }

                        // Convert Bitmap to SoftwareBitmap
                        using (MemoryStream ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Bmp);
                            ms.Position = 0;

                            var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                            // Run OCR
                            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                            if (engine != null)
                            {
                                var result = await engine.RecognizeAsync(softwareBitmap);
                                return result.Text;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OCR Failed: " + ex.Message);
            }
            return "";
        }
    }
}
