using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace Iconic
{
    /// <summary>Windows 11 frosted-glass (acrylic) backdrop, dark mode and rounded corners.</summary>
    internal static class Glass
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private const int GWL_STYLE = -16;
        private const long WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        public static void Apply(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;

            // Remove the Windows title-bar buttons and the colored caption strip.
            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            style &= ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            int none = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref none, sizeof(int));
            none = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref none, sizeof(int));

            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            int dark = 1, corner = DWMWCP_ROUND, backdrop = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
    }

    /// <summary>Gets crisp, large icons straight from the Windows shell.</summary>
    internal static class IconService
    {
        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public static ImageSource Load(AppItem item)
        {
            if (!string.IsNullOrEmpty(item.CustomIcon) && File.Exists(item.CustomIcon))
            {
                try { return LoadImageFile(item.CustomIcon, 128); } catch { }
            }
            return GetShellImage(item.Path, 128) ?? GetFileIcon(item.Path);
        }

        /// <summary>Turns any picked file (image, .ico, .exe, shortcut) into a PNG in Iconic's icon folder.</summary>
        public static string ImportCustomIcon(string itemId, string file)
        {
            BitmapSource source;
            var ext = IOPath.GetExtension(file).ToLowerInvariant();
            try
            {
                if (ext == ".ico")
                {
                    var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                    source = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                }
                else if (ImageExtensions.Contains(ext))
                {
                    source = LoadImageFile(file, 0);
                }
                else
                {
                    source = GetShellImage(file, 256) ?? GetFileIcon(file);
                }
            }
            catch { return null; }

            if (source == null) return null;

            int largest = Math.Max(source.PixelWidth, source.PixelHeight);
            if (largest > 256)
            {
                double scale = 256.0 / largest;
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }

            Directory.CreateDirectory(AppPaths.IconsDir);
            var dest = IOPath.Combine(AppPaths.IconsDir, $"{itemId}-{DateTime.Now.Ticks}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(dest)) encoder.Save(stream);
            return dest;
        }

        private static BitmapSource LoadImageFile(string path, int decodeWidth)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;          // don't lock the file
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }

        // ---- IShellItemImageFactory: large, high-quality icons and thumbnails ----

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int cx; public int cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            string path, IntPtr bindContext, ref Guid riid, out IShellItemImageFactory factory);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint Color0, Color1, Color2;
        }

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines,
            [Out] byte[] bits, ref BITMAPINFO info, uint usage);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        private const int SIIGBF_BIGGERSIZEOK = 0x1;

        public static BitmapSource GetShellImage(string path, int size)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null)
                return null;
            try
            {
                if (factory.GetImage(new NativeSize { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK, out var hbm) != 0
                    || hbm == IntPtr.Zero)
                    return null;
                try { return FromHBitmap(hbm); }
                finally { DeleteObject(hbm); }
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(factory); }
        }

        private static BitmapSource FromHBitmap(IntPtr hbm)
        {
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bmp) == 0) return null;
            int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
            if (w <= 0 || h <= 0) return null;

            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            info.bmiHeader.biWidth = w;
            info.bmiHeader.biHeight = -h; // top-down rows
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;

            var pixels = new byte[w * h * 4];
            var hdc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(hdc, hbm, 0, (uint)h, pixels, ref info, 0) == 0) return null;
            }
            finally { ReleaseDC(IntPtr.Zero, hdc); }

            // Some images come back with no alpha at all; make them opaque instead of invisible.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha)
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
            source.Freeze();
            return source;
        }

        // ---- Fallback: classic 32px shell icon ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private const uint SHGFI_ICON = 0x100;

        private static BitmapSource GetFileIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var info = new SHFILEINFO();
            SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON);
            if (info.hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch { return null; }
            finally { DestroyIcon(info.hIcon); }
        }
    }

    /// <summary>Sends files and folders to the Recycle Bin (recoverable), never a permanent delete.</summary>
    internal static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

        private const uint FO_DELETE = 0x3;
        private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

        public static bool Send(string path)
        {
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0",   // list must end with two nulls
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)
            };
            return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted;
        }
    }

    /// <summary>Loads icons on a background thread so the window opens instantly.</summary>
    internal static class IconLoader
    {
        private static readonly BlockingCollection<AppItem> Queue = new BlockingCollection<AppItem>();

        static IconLoader()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Iconic icon loader" };
            thread.SetApartmentState(ApartmentState.STA); // shell COM needs STA
            thread.Start();
        }

        public static void Enqueue(AppItem item) => Queue.Add(item);

        private static void Run()
        {
            foreach (var item in Queue.GetConsumingEnumerable())
            {
                ImageSource image = null;
                try { image = IconService.Load(item); } catch { }
                if (image != null && image.CanFreeze && !image.IsFrozen) image.Freeze();
                Application.Current?.Dispatcher.InvokeAsync(() => item.Icon = image);
            }
        }
    }
}
