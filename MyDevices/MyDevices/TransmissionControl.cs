using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Windows.Networking.Sockets;
using Windows.System.Threading;

namespace TransmissionCTRL
{
    public delegate void TransmissionControllerDataReceived(object sender, TransmissionControllerDataReceivedEventArgs e);
    public delegate void TransmissionControllerEventDispatcher(object sender, TransmissionControllerEventArgs e);

    public class TransmissionControllerEventArgs
    {
        public TransmissionControllerEvents Event { get; set; }
        public string EventMessage { get; set; }
        public TCPClientState ClientState { get; set; }
    }

    public enum TCPClientState { NOT_RESPONDING, DISCONNECTED, CONNECTED };

    public enum TransmissionControllerEvents { READY_FOR_COMMUNICATION, READ_INTERRUPT, ERROR_OCCURRED };

    public class TransmissionControllerDataReceivedEventArgs
    {
        public TransmissionControllerDataReceivedEventArgs()
        {
            DataFormat = TransmissionDataFormat.PLAIN_TEXT;
            EncryptionType = TransmissionEncryptionType.NONE;
        }

        public string ReceivedData { get; set; }
        public TransmissionDataFormat DataFormat { get; set; }
        public TransmissionEncryptionType EncryptionType { get; set; }
    }

    internal class TransmissionControl
    {
        public TransmissionControl(StreamSocket streamSocket)
        {
            this.StartServerReadLoop(streamSocket.InputStream, streamSocket);

            this.CSocket = streamSocket;

            this.TransmissionQueueControllerTimer =
                ThreadPoolTimer.CreatePeriodicTimer(this.OnTimerElapsed, TimeSpan.FromMilliseconds(500));

            // at first send rsa public key to remote device
            var pKey = this.GetRSAPublicKeyAsBase64();
            if (pKey != null)
            {
                TransmissionPackage package = new();
                if (package != null)
                {
                    package.Data = pKey;
                    package.DataFormat = TransmissionDataFormat.PLAIN_TEXT;
                    package.EncryptionType = TransmissionEncryptionType.NONE;
                    package.Mode = TransmissionMode.RSA_PUBKEY;
                    package.TransmissionID = this.NextTransmissionID++;

                    this.SendTransmissionPackage(package);
                }
            }
        }

        ~TransmissionControl()
        {
            this.TransmissionQueueControllerTimer.Cancel();
        }

        #region fields & properties
        public event TransmissionControllerDataReceived TransmissionControllerDataReceivedEvent;
        public event TransmissionControllerEventDispatcher TransmissionControllerInternalEvents;

        public TCPClientState ClientState { get; private set; } = TCPClientState.CONNECTED;

        private uint NextTransmissionID = 0;
        private readonly List<TransmissionPackage> TransmissionQueue = new();
        private readonly ThreadPoolTimer TransmissionQueueControllerTimer = null;

        private readonly StreamSocket CSocket = null;
        private Stream CServerOutputStream = null;
        private StreamWriter CServerOutputStreamWriter = null;

        private readonly RSA RSASessionObject = RSA.Create();
        private readonly byte[] DecryptedAesKey = new byte[32];
        #endregion

        public void SendData(string data)
        {
            if (data != null)
            {
                TransmissionPackage package = new();
                if (package != null)
                {
                    var iv = CreateRandomIV();

                    package.Data = EncryptDataWithAesCbc(data, iv);
                    package.DataFormat = TransmissionDataFormat.BASE64;
                    package.EncryptionType = TransmissionEncryptionType.AES;
                    package.Mode = TransmissionMode.DATA;
                    package.IV = Convert.ToBase64String(iv);

                    this.SendTransmissionPackage(package);
                }
            }
        }

        private void SendTransmissionPackage(TransmissionPackage package)
        {
            if (package != null)
            {
                package.TransmissionID = this.NextTransmissionID++;

                if (this.TransmissionQueue.Count > 0)
                {
                    // if there are packages in the queue, add this package to the queue, but don't send it yet
                    this.TransmissionQueue.Add(package);
                }
                else
                {
                    // there are no packages in the queue, send this package immediately
                    var dataToSend = package.ToTransmissionString();
                    if (dataToSend != null && !package.ErrorFlag)
                    {
                        this.TransmissionQueue.Add(package);
                        this.SendDataAsync(dataToSend);
                    }
                }
            }
        }

        private async void SendDataAsync(string data)
        {
            try
            {
                if (this.CServerOutputStreamWriter == null)
                {
                    this.CServerOutputStream = this.CSocket.OutputStream.AsStreamForWrite();
                    this.CServerOutputStreamWriter = new StreamWriter(this.CServerOutputStream);
                }

                foreach (char c in data)
                {
                    await this.CServerOutputStreamWriter.WriteAsync(c);
                }

                await this.CServerOutputStreamWriter.FlushAsync();
            }
            catch (Exception exp)
            {
                this.TransmissionControllerInternalEvents?.Invoke(
                    this,
                    new TransmissionControllerEventArgs()
                    {
                        Event = TransmissionControllerEvents.ERROR_OCCURRED,
                        EventMessage = "TransmissionControl::SendDataAsync: " + exp.Message
                    });
            }
        }

        private string EncryptDataWithAesCbc(string data, byte[] _iv)
        {
            try
            {
                var aes = Aes.Create();
                if (aes != null)
                {
                    aes.Key = this.DecryptedAesKey;
                    aes.IV = _iv;
                    var encryptedData = aes.EncryptCbc(Encoding.ASCII.GetBytes(data), _iv, PaddingMode.Zeros);
                    var dataToSend = Convert.ToBase64String(encryptedData);
                    return dataToSend;
                }
                return null;
            }
            catch (Exception e)
            {
                this.TransmissionControllerInternalEvents?.Invoke(
                    this,
                    new TransmissionControllerEventArgs()
                    {
                        Event = TransmissionControllerEvents.ERROR_OCCURRED,
                        EventMessage = "TransmissionControl::EncryptDataWithAesCbc: " + e.Message
                    });
                return null;
            }
        }

        private string DecryptDataWithAesCbc(string data, string iv)
        {
            try
            {
                if (data != null && iv != null)
                {
                    byte[] bIV = Convert.FromBase64String(iv);
                    if (bIV != null)
                    {
                        var aes = Aes.Create();
                        if (aes != null)
                        {
                            aes.Key = this.DecryptedAesKey;
                            aes.IV = bIV;
                            var decryptedData = aes.DecryptCbc(Convert.FromBase64String(data), bIV, PaddingMode.Zeros);
                            var plainData = Encoding.ASCII.GetString(decryptedData);
                            return plainData;
                        }
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                this.TransmissionControllerInternalEvents?.Invoke(
                    this,
                    new TransmissionControllerEventArgs()
                    {
                        Event = TransmissionControllerEvents.ERROR_OCCURRED,
                        EventMessage = "TransmissionControl::DecryptDataWithAesCbc: " + e.Message,
                        ClientState = this.ClientState
                    }
                );
                return null;
            }
        }

        private async void StartServerReadLoop(Windows.Storage.Streams.IInputStream inputStream, StreamSocket socket)
        {
            try
            {
                using (var streamReader = new StreamReader(inputStream.AsStreamForRead()))
                {
                    while (true)
                    {
                        string data = await streamReader.ReadLineAsync();
                        if (data != null)
                        {
                            if (data.Length > 0)
                            {
                                this.InternalDataProcessing(data);
                            }
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                // this exception is expected when the client connection got terminated unexpectedly
                this.ClientState = TCPClientState.DISCONNECTED;

                // only report as error if this was an unexpected exception
                var _event = (exp.HResult == -2147014836) ? TransmissionControllerEvents.READ_INTERRUPT : TransmissionControllerEvents.ERROR_OCCURRED;

                this.TransmissionControllerInternalEvents?.Invoke(
                    this,
                    new TransmissionControllerEventArgs()
                    {
                        Event = _event,
                        EventMessage = "TransmissionControl::StartServerReadLoop: " + exp.Message,
                        ClientState = this.ClientState
                    }
                );
            }
        }

        private void InternalDataProcessing(string data)
        {
            if (data != null)
            {
                TransmissionPackage package = new TransmissionPackage();
                if (package != null)
                {
                    package.FromTransmissionString(data);
                    if (package.ErrorFlag == false)
                    {
                        if (package.Mode == TransmissionMode.AES_KEY)
                        {
                            try
                            {
                                var dataToDecrypt = package.Data;
                                if (dataToDecrypt != null)
                                {
                                    var decryptedData = this.RSASessionObject.Decrypt(Convert.FromBase64String(dataToDecrypt), RSAEncryptionPadding.Pkcs1);
                                    if (decryptedData != null)
                                    {
                                        int counter = 0;

                                        foreach (byte b in decryptedData)
                                        {
                                            if (counter < 32)
                                            {
                                                this.DecryptedAesKey[counter] = b;
                                            }
                                            else
                                            {
                                                break;
                                            }
                                            counter++;
                                        }
                                        this.TransmissionControllerInternalEvents?.Invoke(this, new TransmissionControllerEventArgs() { Event = TransmissionControllerEvents.READY_FOR_COMMUNICATION });
                                    }
                                }
                                // confirm reception
                                this.SendDataAsync(package.ToConfirmationString());
                            }
                            catch (Exception e)
                            {
                                this.TransmissionControllerInternalEvents?.Invoke(
                                    this,
                                    new TransmissionControllerEventArgs()
                                    {
                                        Event = TransmissionControllerEvents.ERROR_OCCURRED,
                                        EventMessage = "TransmissionControl::InternalDataProcessing:DecryptAES-Section: " + e.Message,
                                        ClientState = this.ClientState
                                    }
                                );
                            }
                        }
                        else if (package.Mode == TransmissionMode.CONFIRM)
                        {
                            RemovePackageFromQueueAndSendNext(package.TransmissionID);
                        }
                        else if (package.Mode == TransmissionMode.DATA)
                        {
                            try
                            {
                                if (package.EncryptionType == TransmissionEncryptionType.NONE)
                                {
                                    this.TransmissionControllerDataReceivedEvent?.Invoke(this, new TransmissionControllerDataReceivedEventArgs() { ReceivedData = package.Data, DataFormat = package.DataFormat });
                                }
                                else if (package.EncryptionType == TransmissionEncryptionType.AES)
                                {
                                    var enc_data = this.DecryptDataWithAesCbc(package.Data, package.IV);
                                    if (enc_data != null)
                                    {
                                        this.TransmissionControllerDataReceivedEvent?.Invoke(this, new TransmissionControllerDataReceivedEventArgs() { ReceivedData = enc_data });
                                    }
                                }
                                // confirm reception
                                this.SendDataAsync(package.ToConfirmationString());
                            }
                            catch (Exception exp)
                            {
                                this.TransmissionControllerInternalEvents?.Invoke(
                                    this,
                                    new TransmissionControllerEventArgs()
                                    {
                                        Event = TransmissionControllerEvents.ERROR_OCCURRED,
                                        EventMessage = "TransmissionControl::InternalDataProcessing:Data-Section: " + exp.Message,
                                        ClientState = this.ClientState
                                    }
                                );
                            }
                        }
                    }
                    else
                    {
                        this.TransmissionControllerInternalEvents?.Invoke(
                            this,
                            new TransmissionControllerEventArgs()
                            {
                                Event = TransmissionControllerEvents.ERROR_OCCURRED,
                                EventMessage = "TransmissionControl::InternalDataProcessing: Error while decoding transmission string to transmission package object",
                                ClientState = this.ClientState
                            }
                        );
                    }
                }
            }
        }

        private void RemovePackageFromQueueAndSendNext(uint packageID)
        {
            foreach (var package in this.TransmissionQueue)
            {
                if (package.TransmissionID == packageID)
                {
                    this.TransmissionQueue.Remove(package);
                    break;
                }
            }
            // send the next package in the queue (if applicable)
            if (this.TransmissionQueue.Count > 0)
            {
                this.SendDataAsync(this.TransmissionQueue[0].ToTransmissionString());
            }
        }

        private string GetRSAPublicKeyAsBase64()
        {
            // export the key from the rsa class in x509 format and convert it to a base64 string
            string result;
            var keyInfo = this.RSASessionObject.ExportSubjectPublicKeyInfo();
            result = Convert.ToBase64String(keyInfo, Base64FormattingOptions.InsertLineBreaks);
            return result;
        }

        private static byte[] CreateRandomIV()
        {
            byte[] result = new byte[16];
            Random rnd = new();
            rnd.NextBytes(result);
            return result;
        }

        private void OnTimerElapsed(ThreadPoolTimer timer)
        {
            try
            {
                if (this.ClientState == TCPClientState.CONNECTED)
                {
                    // check if there are unconfirmed packages in the queue
                    if (this.TransmissionQueue.Count > 0)
                    {
                        // check if the first package in the queue was previously marked as unconfirmed
                        if (this.TransmissionQueue[0].ConfirmationControlValue == 0)
                        {
                            // in the first instance, mark the package as unconfirmed
                            this.TransmissionQueue[0].ConfirmationControlValue = 1;
                        }
                        else
                        {
                            if (this.TransmissionQueue[0].ConfirmationControlValue >= 4)
                            {
                                // if the package was sent 3 times, remove it from the queue
                                this.TransmissionQueue.RemoveAt(0);

                                // if there are still packages in the queue, send the next one
                                if (this.TransmissionQueue.Count > 0)
                                {
                                    this.SendDataAsync(this.TransmissionQueue[0].ToTransmissionString());
                                }
                            }
                            else
                            {
                                this.TransmissionQueue[0].ConfirmationControlValue++;

                                // send the package again
                                this.SendDataAsync(this.TransmissionQueue[0].ToTransmissionString());
                            }
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                //(App.Current as App).AddLogEntry("Transmission Queue Controller Exception: " + exp.Message, LogEntrySeverity.ERROR);
                this.TransmissionControllerInternalEvents?.Invoke(
                    this,
                    new TransmissionControllerEventArgs()
                    {
                        Event = TransmissionControllerEvents.ERROR_OCCURRED,
                        EventMessage = "TransmissionControl::OnTimerElapsed: " + exp.Message,
                        ClientState = this.ClientState
                    }
                );
            }
        }
    }
}

