using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MyDevices
{
    public delegate void LogEntryAdded(string logText, LogEntrySeverity severity);

    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        public void AddLogEntry(string logEntryText, LogEntrySeverity severity)
        {
            LogEntry_Added?.Invoke(logEntryText, severity);
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }

        public string GetStringFromResource(string resourceID)
        {
            var loader = new ResourceLoader();
            return loader?.GetString(resourceID) ?? "Error not found";
        }

        private Window m_window;
        public event LogEntryAdded LogEntry_Added;
    }
}
