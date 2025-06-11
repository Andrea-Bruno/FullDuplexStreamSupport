using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.Diagnostics;

namespace FullDuplexStreamSupport
{
    /// <summary>
    /// Represents a full-duplex communication stream using various data streaming systems.
    /// This class provides methods for reading and writing data asynchronously and synchronously,
    /// managing client connections, and handling data transmission with a specified timeout.
    /// </summary>
    public class PipeStream
    {
        /// <summary>
        /// Initializes the pipe stream as a server with the specified input and output streams.
        /// </summary>
        /// <param name="pipeIn">Input stream</param>
        /// <param name="pipeOut">Output stream</param>
        /// <param name="onNewClient">Action to invoke when a new client connects</param>
        public void InitializeServer(Stream pipeIn, Stream pipeOut, Action<PipeStreamClient>? onNewClient = null)
        {
            IsListener = true;
            var manualEvent = new ManualResetEvent(false);
            Task.Run(() => Initialize(pipeIn, pipeOut, onNewClient, manualResetEvent: manualEvent));
            manualEvent.WaitOne();
        }

        /// <summary>
        /// Initializes the pipe stream as a client with the specified input and output streams.
        /// </summary>
        /// <param name="pipeIn">Input stream</param>
        /// <param name="pipeOut">Output stream</param>
        /// <param name="connectTimeOutMs">Optional timeout for connection in milliseconds</param>
        public void InitializeClient(Stream pipeIn, Stream pipeOut, int? connectTimeOutMs = null)
        {
            Initialize(pipeIn, pipeOut, connectTimeOutMs: connectTimeOutMs);
        }

        /// <summary>
        /// Initializes the pipe stream with the specified input and output streams.
        /// Re-initialization causes reset.
        /// </summary>
        /// <param name="pipeIn">Input stream</param>
        /// <param name="pipeOut">Output stream</param>
        /// <param name="onNewClient">Action to invoke when a new client connects</param>
        /// <param name="connectTimeOutMs">Optional timeout for connection in milliseconds</param>
        /// <param name="manualResetEvent">Optional manual reset event to signal when the initialization is completed</param>
        private void Initialize(Stream pipeIn, Stream pipeOut, Action<PipeStreamClient>? onNewClient = null, int? connectTimeOutMs = null, ManualResetEvent? manualResetEvent = null)
        {
            lock (_clientList)
            {
                Stop();
                Start();
                OnNewClient = onNewClient;

                PipeIn?.Dispose();
                PipeIn = pipeIn;
                CallConnect(pipeIn, connectTimeOutMs);

                PipeOut?.Dispose();
                PipeOut = pipeOut;
                CallConnect(pipeOut, connectTimeOutMs);

                WaitForConnection(pipeIn, pipeOut, manualResetEvent);

                DataError = false;
                TaskReader = Task.Run(() => StartDataReader());
            }
        }

        /// <summary>
        /// Calls the Connect method on the stream with an optional timeout.
        /// </summary>
        /// <param name="stream">The stream to connect</param>
        /// <param name="connectTimeOutMs">Optional timeout for connection in milliseconds</param>
        private static void CallConnect(Stream stream, int? connectTimeOutMs)
        {
            Type objectType = stream.GetType();
            MethodInfo? methodInfo = null;

            if (connectTimeOutMs == null)
            {
                methodInfo = objectType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Connect" && m.GetParameters().Length == 0);
            }
            else
            {
                methodInfo = objectType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Connect" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));
            }

            methodInfo?.Invoke(stream, connectTimeOutMs == null ? null : new object[] { connectTimeOutMs });
        }

        /// <summary>
        /// Waits for the connection to be established for both input and output streams.
        /// </summary>
        /// <param name="pipeIn">Input stream</param>
        /// <param name="pipeOut">Output stream</param>
        /// <param name="manualResetEvent">Optional manual reset event to signal when the initialization is completed</param>
        private static void WaitForConnection(Stream pipeIn, Stream pipeOut, ManualResetEvent? manualResetEvent = null)
        {
            bool pipeInConnected = false;
            bool pipeOutConnected = false;

            Type pipeInType = pipeIn.GetType();
            Type pipeOutType = pipeOut.GetType();

            MethodInfo? waitForConnectionIn = pipeInType.GetMethod("WaitForConnection");
            MethodInfo? waitForConnectionOut = pipeOutType.GetMethod("WaitForConnection");

            Task? waitForConnectionInTask = null;
            Task? waitForConnectionOutTask = null;

            if (waitForConnectionIn != null)
            {
                waitForConnectionInTask = Task.Run(() =>
                {
                    waitForConnectionIn.Invoke(pipeIn, null);
                    pipeInConnected = true;
                });
            }
            else
            {
                pipeInConnected = true;
            }

            if (waitForConnectionOut != null)
            {
                waitForConnectionOutTask = Task.Run(() =>
                {
                    waitForConnectionOut.Invoke(pipeOut, null);
                    pipeOutConnected = true;
                });
            }
            else
            {
                pipeOutConnected = true;
            }
            manualResetEvent?.Set();
            Task.WaitAll(new Task[] { waitForConnectionInTask ?? Task.CompletedTask, waitForConnectionOutTask ?? Task.CompletedTask });

            if (!pipeInConnected || !pipeOutConnected)
            {
                throw new InvalidOperationException("Failed to establish connection for one or both streams.");
            }
        }

        /// <summary>
        /// Gets a value indicating whether the pipe stream is initialized.
        /// </summary>
        public bool IsInitialized
        {
            get
            {
                lock (_clientList)
                {
                    return PipeIn != null && PipeOut != null;
                }
            }
        }
        internal uint NextClientId;
        private Action<PipeStreamClient>? OnNewClient;
        public bool AcceptNewClient = true;
        internal readonly Dictionary<uint, PipeStreamClient> _clientList = new Dictionary<uint, PipeStreamClient>();
        private Task? TaskReader;

        /// <summary>
        /// Starts the data reader thread to handle incoming data.
        /// </summary>
        private async Task StartDataReader()
        {
            var pipeIn = PipeIn;
            try
            {
                if (pipeIn is null)
                    PipeisConnected = false;
                else
                {
                    pipeIn.Read(new byte[0]);
                    PipeisConnected = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Debugger.Break();
                PipeisConnected = false;
            }
            var taskId = TaskReader?.Id;
            while (taskId == TaskReader?.Id)
            {
                try
                {
                    if (pipeIn is null)
                    {
                        throw new InvalidOperationException("PipeIn is null");
                    }
                    else
                    {
                        var startBytes = new byte[5];
                        pipeIn.Read(startBytes, 0, 5);
                        PipeisConnected = true;
                        var dataType = startBytes[0];
                        if (dataType == (byte)DataType.NewClient)
                        {
                            if (AcceptNewClient)
                            {
                                uint clientID = BitConverter.ToUInt32(startBytes, 1);
                                AddNewClient(clientID);
                            }
                        }
                        else if (dataType == (byte)DataType.DataTransmission)
                        {
                            var dataLength = BitConverter.ToInt32(startBytes, 1);
                            var data = new byte[dataLength];
                            pipeIn.Read(data, 0, dataLength);
                            lock (_clientList)
                            {
                                var clientId = BitConverter.ToUInt32(data, 0);
                                if (_clientList.TryGetValue(clientId, out var server))
                                {
                                    server.AddDataToRead(data.Skip(4).ToArray());
                                }
                            }
                        }
                        else
                        {
                            Debugger.Break();
                            DataError = true;
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PipeisConnected = false;
                    await Task.Delay(1000);
                }
            }
        }

        /// <summary>
        /// Adds a new client to the server if this instance is a listener, otherwise add a client in client side which acts as a communication stream.
        /// </summary>
        /// <param name="clientId">Client id: The id must be specified only if the client is created by the listener and will be the one communicated with the connection event</param>
        /// <returns></returns>
        public PipeStreamClient AddNewClient(uint? clientId = null)
        {
            if (clientId != null && !IsListener)
            {
                throw new InvalidOperationException("The client id must be specified only if the client is created by the listener.");
            }
            var newPipeStream = new PipeStreamClient(this, clientId);
            _clientList.Add(newPipeStream.Id, newPipeStream);
            OnNewClient?.Invoke(newPipeStream);
            return newPipeStream;
        }
        private CountdownEvent _PipeisConnectedIsUpdated = new CountdownEvent(1);
        private bool IsListener;

        private bool _PipeisConnected;
        internal bool PipeisConnected
        {
            get
            {
                lock (_PipeisConnectedIsUpdated)
                {
                    if (!_PipeisConnected)
                    {
                        _PipeisConnectedIsUpdated.Reset(1);
                        _PipeisConnectedIsUpdated.Wait(1000);
                    }
                }
                return _PipeisConnected;
            }
            private set
            {
                if (value != _PipeisConnected)
                {
                    _PipeisConnected = value;
                    try { if (_PipeisConnectedIsUpdated.CurrentCount != 0) _PipeisConnectedIsUpdated.Signal(); } catch (Exception) { }
                }

            }
        }
        internal enum DataType : byte
        {
            NewClient = 0,
            DataTransmission = 1,
        }

        internal Stream? PipeIn;
        internal Stream? PipeOut;
        internal bool DataError = false;

        /// <summary>
        /// Starts accepting client connections.
        /// </summary>
        public void Start()
        {
            AcceptNewClient = true;
        }

        /// <summary>
        /// Stops accepting client connections and disposes all server streams.
        /// </summary>
        public void Stop()
        {
            AcceptNewClient = false;
            foreach (var client in _clientList.Values)
            {
                client.Dispose();
            }
            _clientList.Clear();
        }
    }

}