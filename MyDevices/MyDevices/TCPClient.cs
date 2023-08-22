using System;
using Windows.Networking.Sockets;
using Windows.System.Threading;

namespace TransmissionCTRL
{
    public delegate void TCPServerDataReceived(TCPClient tcpClient, string data);
    public delegate void TCPClientNameUpdated(TCPClient tcpClient, string name);
    public delegate void TCPClientConnectionStateChanged(TCPClient tcpClient, TCPClientState state);
    public delegate void TCPClientErrorOccurred(TCPClient tcpClient, string message);
    public class TCPClient
    {
        public TCPClient(StreamSocket streamSocket)
        {
            this.TransmissionController = new TransmissionControl(streamSocket);
            this.TransmissionController.TransmissionControllerDataReceivedEvent += OnTransmissionController_DataReceived;
            this.TransmissionController.TransmissionControllerInternalEvents += OnTransmissionController_InternalEvent;

            this.IPAddress = streamSocket.Information.RemoteAddress.DisplayName;

            this.StatusTimer =
                ThreadPoolTimer.CreatePeriodicTimer(this.OnTimerElapsed, TimeSpan.FromMilliseconds(1000));
        }

        private void OnTransmissionController_InternalEvent(object sender, TransmissionControllerEventArgs e)
        {
            if (e.Event == TransmissionControllerEvents.READY_FOR_COMMUNICATION)
            {
                // start name resolution
                this.IsReadyForCommunication = true;
                this.TransmissionController.SendData("get-name");
            }
            else if (e.Event == TransmissionControllerEvents.READ_INTERRUPT)
            {
                this.ClientState = e.ClientState;
            }
            else if (e.Event == TransmissionControllerEvents.ERROR_OCCURRED)
            {
                this.ClientState = e.ClientState;
                this.ErrorOccurred?.Invoke(this, e.EventMessage);
            }
            else
            {
                this.ClientState = e.ClientState;
            }
        }

        private void OnTransmissionController_DataReceived(object sender, TransmissionControllerDataReceivedEventArgs e)
        {
            if (this.DataProcessing(e.ReceivedData) == false)
            {
                this.DataReceived?.Invoke(this, e.ReceivedData);
            }
        }

        ~TCPClient()
        {
            this.StatusTimer.Cancel();
        }

        #region fields & properties

        public event TCPServerDataReceived DataReceived;
        public event TCPClientNameUpdated NameUpdated;
        public event TCPClientConnectionStateChanged ConnectionStateChange;
        public event TCPClientErrorOccurred ErrorOccurred;

        private readonly ThreadPoolTimer StatusTimer = null;
        private readonly TransmissionControl TransmissionController = null;

        private bool IsNameSet = false;
        private int HeartbeatCounter = 0;
        private bool IsReadyForCommunication = false;
        private TCPClientState _clientState = TCPClientState.CONNECTED;

        public string IPAddress { get; private set; }
        public string DeviceName { get; private set; }

        public TCPClientState ClientState
        {
            get => _clientState;
            private set
            {
                if (_clientState != value)
                {
                    _clientState = value;
                    this.ConnectionStateChange?.Invoke(this, _clientState);
                }
            }
        }
        #endregion

        private bool DataProcessing(string data)
        {
            // NOTE: this method returns true if the data was processed, false otherwise

            if (data.StartsWith("set-name:"))
            {
                try
                {
                    this.DeviceName = data.Substring(9);
                    this.IsNameSet = true;

                    // notify subscriber
                    if (this.NameUpdated != null)
                    {
                        this.NameUpdated(this, this.DeviceName);
                    }
                    else
                    {
                        this.IsNameSet = false;
                    }
                    return true;
                }
                catch (Exception)
                {
                    this.IsNameSet = false;
                    return true;
                }
            }
            else if (data.StartsWith("rs:status:active"))
            {
                HeartbeatCounter = 0;

                if (this.ClientState != TCPClientState.CONNECTED)
                {
                    this.ClientState = TCPClientState.CONNECTED;
                    this.ConnectionStateChange?.Invoke(this, TCPClientState.CONNECTED);
                }
                return true;
            }
            return false;
        }

        public void SendData(string _data)
        {
            this.TransmissionController?.SendData(_data);
        }

        private void OnTimerElapsed(ThreadPoolTimer timer)
        {
            if (this.ClientState == TCPClientState.DISCONNECTED)
            {
                return;
            }
            else
            {
                if (this.IsReadyForCommunication)
                {
                    if (this.IsNameSet == false)
                    {
                        this.TransmissionController?.SendData("get-name");
                    }
                    else
                    {
                        this.TransmissionController?.SendData("rq:status");
                        this.HeartbeatCounter++;

                        if (HeartbeatCounter > 2 && this.ClientState == TCPClientState.CONNECTED)
                        {
                            this.ClientState = TCPClientState.NOT_RESPONDING;
                        }
                    }
                }
            }
        }
    }
}
