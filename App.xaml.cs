using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XaiNet2.Helpers;

namespace XaiNet2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Per-user named mutex: prevents a second instance (and a duplicate tray icon).
        private const string SingleInstanceMutexName = "Local\\XaiNet2_SingleInstance";
        private Mutex _singleInstanceMutex;
        private bool _ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                // Another instance is already running; exit quietly without showing a window.
                Shutdown();
                return;
            }

            // Capture crashes to the log (no-op unless logging is enabled in Options). Registered
            // before the main window is built so startup failures are caught too.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            Logger.LogStartupBanner();

            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Log, then let it propagate (behaviour unchanged — we're only capturing, not suppressing).
            Logger.Error("Unhandled UI-thread exception", e.Exception);
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error($"Unhandled exception (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Unobserved task exception", e.Exception);
            e.SetObserved(); // keep a faulted background Task from tearing down the process
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info($"XaiNet2 exiting (code {e.ApplicationExitCode})");
            if (_ownsMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
