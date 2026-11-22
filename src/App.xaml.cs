using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;

namespace Iconic
{
    public partial class App : Application
    {
        private const string MutexName = "Iconic.SingleInstance.v1";
        private const string ShowEventName = "Iconic.ShowPanel.v1";

        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private MainWindow _main;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                ReportCrash(args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportCrash(args.ExceptionObject as Exception);

            // Iconic stays running quietly after the first launch, so every
            // click on the desktop icon opens the panel instantly.
            _mutex = new Mutex(true, MutexName, out bool isFirst);
            if (!isFirst)
            {
                try
                {
                    AllowSetForegroundWindow(-1);
                    using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                    signal.Set();
                }
                catch { }
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.InvokeAsync(() => _main?.ShowPanel());
                }
            }) { IsBackground = true, Name = "Iconic show listener" };
            listener.Start();

            _main = new MainWindow();
            bool background = e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (background) _main.Prepare();
            else _main.ShowPanel();
        }
    }

    public partial class App
    {
        private static bool _reported;

        /// <summary>Saves the error to %APPDATA%\Iconic\crash.log and shows it, so problems can be fixed.</summary>
        private static void ReportCrash(Exception ex)
        {
            if (ex == null || _reported) return;
            _reported = true;
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.AppendAllText(System.IO.Path.Combine(AppPaths.DataDir, "crash.log"),
                    $"{DateTime.Now}\r\n{ex}\r\n\r\n");
            }
            catch { }
            MessageBox.Show(ex.ToString(), "Iconic hit a problem", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public class InverseBoolToVis : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
