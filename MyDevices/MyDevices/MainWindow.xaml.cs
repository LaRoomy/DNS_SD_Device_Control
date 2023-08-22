using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TransmissionCTRL;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.ServiceDiscovery.Dnssd;
using Windows.Networking.Sockets;

namespace MyDevices
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            this.Activated += MainWindow_Activated;

            AppAccessor.LogEntry_Added += MainWindow_OnLogEntryAdded;
        }

        private void MainWindow_OnLogEntryAdded(string logText, LogEntrySeverity severity)
        {
            try
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    this.LogListView.Items.Add(new LogListEntryDataModel(logText, severity));

                    if (this.LogListView.Items.Count > 100)
                    {
                        this.LogListView.Items.RemoveAt(0);
                    }
                });
            }
            catch (Exception e)
            {
                Debug.WriteLine("LogList-Exception: " + e.Message);
            }
        }

        private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                this.HeaderAppImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/chip_grayscale.png"));
                this.AppTitleTextBlock.Foreground = App.Current.Resources["TitleBarDeactivatedTextColorBrush"] as SolidColorBrush;
            }
            else
            {
                this.HeaderAppImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/chip.png"));
                this.AppTitleTextBlock.Foreground = App.Current.Resources["TitleBarActivatedTextColorBrush"] as SolidColorBrush;
            }
        }

        #region Fields
        private DnssdServiceInstance DnssdServiceInstance = null;
        private StreamSocketListener SSocketListener = null;
        private readonly List<TCPClient> TCPClientList = new();
        private readonly ObservableCollection<RemoteDeviceInfo> UIDeviceList = new();

        // The default instance name when registering the DNS-SD service.
        private const string INSTANCE_NAME = "mydevices";

        // The network protocol that will be accepting connections for responses.
        private const string NETWORK_PROTOCOL = "_tcp";

        // The domain of the DNS-SD registration.
        private const string DOMAIN = "local";

        // The service type of the DNS-SD registration.
        private const string SERVICE_TYPE = "_mydevices";

        // The default port to use when connecting back to the manager.
        private const string DEFAULT_PORT = "1337";

        private static string DnsServiceInstanceName => $"{INSTANCE_NAME}.{SERVICE_TYPE}.{NETWORK_PROTOCOL}.{DOMAIN}.";
        private static App AppAccessor => App.Current as App;
        #endregion

        async void StartTCPSocketServerAsync()
        {
            try
            {
                this.SSocketListener = new StreamSocketListener();

                if (this.SSocketListener != null)
                {
                    this.SSocketListener.ConnectionReceived += StreamSocketListener_ConnectionReceivedAsync;

                    // Start listening for incoming TCP connections on the specified port.
                    await this.SSocketListener.BindServiceNameAsync(DEFAULT_PORT);
                    AppAccessor.AddLogEntry("Listener started..", LogEntrySeverity.INFO);

                    //Register the dns service instance
                    this.DnssdServiceInstance =
                        new DnssdServiceInstance(
                                DnsServiceInstanceName,
                                NetworkInformation.GetHostNames()
                                    .FirstOrDefault(x => x.Type == HostNameType.DomainName && x.RawName.Contains("local")),
                                UInt16.Parse(this.SSocketListener.Information.LocalPort)
                            );

                    var status = await this.DnssdServiceInstance.RegisterStreamSocketListenerAsync(this.SSocketListener);

                    if (status.Status == DnssdRegistrationStatus.Success)
                    {
                        AppAccessor.AddLogEntry("Listener Registered. DnssdServiceInstance Name: " + this.DnssdServiceInstance.DnssdServiceInstanceName, LogEntrySeverity.INFO);
                    }
                }
            }
            catch (Exception e)
            {
                AppAccessor.AddLogEntry("Exception Occurred while starting the tcp socket server:" + e.Message, LogEntrySeverity.ERROR);
            }
        }

        private void StreamSocketListener_ConnectionReceivedAsync(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            (Application.Current as App).AddLogEntry("Listener received connection to device with ip: " + args.Socket.Information.RemoteAddress.DisplayName, LogEntrySeverity.INFO);

            var remoteDeviceInfo = new RemoteDeviceInfo(args.Socket.Information.RemoteHostName.DisplayName, args.Socket.Information.RemoteAddress.DisplayName);

            DispatcherQueue?.TryEnqueue(()
                => this.UIDeviceList.Add(remoteDeviceInfo)
            );

            var tcpClient = new TCPClient(args.Socket);
            if (tcpClient != null)
            {
                tcpClient.DataReceived += TcpClient_OnDataReceived;
                tcpClient.NameUpdated += TcpClient_OnNameUpdated;
                tcpClient.ConnectionStateChange += TcpClient_OnConnectionStateChange;

                TCPClientList.Add(tcpClient);
            }
        }

        private void TcpClient_OnConnectionStateChange(TCPClient tcpClient, TCPClientState state)
        {
            if (state == TCPClientState.DISCONNECTED)
            {
                Task.Run(async delegate
                {
                    // restart the listener
                    this.SSocketListener.Dispose();
                    this.SSocketListener = null;
                    this.DnssdServiceInstance = null;

                    await Task.Delay(500);
                    this.StartTCPSocketServerAsync();
                });

                var indexToRemove = -1;

                // remove the item from the list
                foreach (var remoteDevice in UIDeviceList)
                {
                    if (remoteDevice.IPAddress == tcpClient.IPAddress)
                    {
                        DispatcherQueue?.TryEnqueue(() => UIDeviceList.Remove(remoteDevice));
                        this.TCPClientList.Remove(tcpClient);
                        AppAccessor.AddLogEntry("Device " + tcpClient.DeviceName + " with ip: " + tcpClient.IPAddress + " was removed due to disconnected state.", LogEntrySeverity.ERROR);
                        return;
                    }
                    indexToRemove++;
                }
            }
            else if (state == TCPClientState.NOT_RESPONDING)
            {
                // show the waring icon in the ListViewItem
                foreach (var remoteDevice in UIDeviceList)
                {
                    if (remoteDevice.IPAddress == tcpClient.IPAddress)
                    {
                        DispatcherQueue?.TryEnqueue(() => remoteDevice.WarningIconVisibility = Visibility.Visible);
                        AppAccessor.AddLogEntry("Device " + tcpClient.DeviceName + " with ip: " + tcpClient.IPAddress + " stopped responding!", LogEntrySeverity.WARNING);
                        return;
                    }
                }
            }
            else if (state == TCPClientState.CONNECTED)
            {
                // hide the warning icon in the ListViewItem
                foreach (var remoteDevice in UIDeviceList)
                {
                    if (remoteDevice.IPAddress == tcpClient.IPAddress)
                    {
                        DispatcherQueue?.TryEnqueue(() => remoteDevice.WarningIconVisibility = Visibility.Collapsed);
                        return;
                    }
                }
            }
        }

        private void TcpClient_OnNameUpdated(TCPClient tcpClient, string name)
        {
            foreach (var remoteDevice in UIDeviceList)
            {
                if (remoteDevice.IPAddress == tcpClient.IPAddress)
                {
                    var result = DispatcherQueue?.TryEnqueue(() => remoteDevice.Name = name);
                    if (result == false)
                    {
                        throw new Exception("Failed to update the name of the device in the ListView");
                    }
                    AppAccessor.AddLogEntry("Name of device updated. New name: " + tcpClient.DeviceName + " (IP: " + tcpClient.IPAddress + ")", LogEntrySeverity.INFO);
                    return;
                }
            }
        }

        private void TcpClient_OnDataReceived(TCPClient tcpClient, string data)
        {
            try
            {
                AppAccessor.AddLogEntry("Data received from: " + tcpClient.DeviceName + " -> data: " + data, LogEntrySeverity.INFO);
            }
            catch (Exception)
            {
                return;
            }
        }


        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceList.SelectedIndex >= 0)
            {
                SetDeviceSelection(((RemoteDeviceInfo)DeviceList.SelectedItem).Name);
            }
            else
            {
                SetDeviceSelection(null);
            }
        }

        private void SetDeviceSelection(string deviceName)
        {
            if (deviceName != null)
            {
                if (deviceName.Length > 0)
                {
                    if (DeviceList.SelectedIndex >= 0)
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            CurrentSelectedDeviceTextBlock.Text = ((RemoteDeviceInfo)DeviceList.SelectedItem).Name;
                            CurrentSelectedDeviceTextBlock.Foreground = App.Current.Resources["SelectedDeviceColor"] as SolidColorBrush;
                        });
                        return;
                    }
                }
            }
            // no device selected
            DispatcherQueue?.TryEnqueue(() =>
            {
                CurrentSelectedDeviceTextBlock.Text = AppAccessor.GetStringFromResource("UIString_NoDeviceSelected");
                CurrentSelectedDeviceTextBlock.Foreground = App.Current.Resources["NoDeviceSelectedColor"] as SolidColorBrush;
            });
        }

        private void SendDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DeviceList.SelectedIndex >= 0)
                {
                    var data = DataInputTextBox.Text;
                    if (data.Length > 0)
                    {
                        DataInputTextBox.Text = "";

                        var deviceItem = DeviceList.Items.ElementAt(DeviceList.SelectedIndex) as RemoteDeviceInfo;

                        foreach (var client in TCPClientList)
                        {
                            if (client.IPAddress == deviceItem.IPAddress)
                            {
                                if (client.ClientState == TCPClientState.NOT_RESPONDING)
                                {
                                    AppAccessor.AddLogEntry("Client not responding. Send operation failed.", LogEntrySeverity.WARNING);
                                }
                                else
                                {
                                    client.SendData(data);
                                }
                            }
                        }
                    }
                    else
                    {
                        AppAccessor.AddLogEntry("No data to send was entered !", LogEntrySeverity.WARNING);
                    }
                }
                else
                {
                    AppAccessor.AddLogEntry("No device is selected !", LogEntrySeverity.WARNING);
                }
            }
            catch (Exception ex)
            {
                (Application.Current as App).AddLogEntry("Exception Occurred while trying to send data:" + ex.Message, LogEntrySeverity.ERROR);
            }
        }

        private void DeviceList_Loaded(object sender, RoutedEventArgs e)
        {
            StartTCPSocketServerAsync();
        }

        private void DataInputTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SendDataButton_Click(null, null);
            }
        }
    }

    class RemoteDeviceInfo : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _ipAddress;
        public string IPAddress
        {
            get => _ipAddress;
            set
            {
                if (_ipAddress != value)
                {
                    _ipAddress = value;
                    OnPropertyChanged();
                }
            }
        }

        private Visibility _visibility;
        public Visibility WarningIconVisibility
        {
            get => _visibility;
            set
            {
                if (_visibility != value)
                {
                    _visibility = value;
                    OnPropertyChanged();
                }
            }
        }

        public RemoteDeviceInfo(string name, string ipAddress)
        {
            Name = name;
            IPAddress = ipAddress;
            WarningIconVisibility = Visibility.Collapsed;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public enum LogEntrySeverity
    {
        INFO,
        WARNING,
        ERROR
    }

    class LogListEntryDataModel
    {
        public LogListEntryDataModel(string logEntry, LogEntrySeverity severity)
        {
            this.LogEntry = logEntry;
            this.Severity = severity;
        }

        public string LogEntry = "<none>";
        public LogEntrySeverity Severity = LogEntrySeverity.INFO;

        public BitmapImage SeverityIcon
        {
            get
            {
                if (this.Severity == LogEntrySeverity.WARNING)
                {
                    return new BitmapImage(new Uri("ms-appx:///Assets/warning_sq24.png"));
                }
                else if (this.Severity == LogEntrySeverity.ERROR)
                {
                    return new BitmapImage(new Uri("ms-appx:///Assets/error_sq24.png"));
                }
                else
                {
                    return new BitmapImage(new Uri("ms-appx:///Assets/info_sq24.png"));
                }
            }
        }

        public SolidColorBrush TextBrush
        {
            get
            {
                if (this.Severity == LogEntrySeverity.WARNING)
                {
                    return App.Current.Resources["LogListWarningTextBrush"] as SolidColorBrush;
                }
                else if (this.Severity == LogEntrySeverity.ERROR)
                {
                    return App.Current.Resources["LogListErrorTextBrush"] as SolidColorBrush;
                }
                else
                {
                    return App.Current.Resources["LogListInfoTextBrush"] as SolidColorBrush;
                }
            }
        }
    }
}
