using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace FullDuplexStreamSupport
{
    /// <summary>
    /// Represents a full-duplex communication stream using named pipes.
    /// This class provides methods for reading and writing data asynchronously and synchronously,
    /// managing client connections, and handling data transmission with a specified timeout.
    /// </summary>
    public class PipeStream : Stream, IDisposable
    {
        /// <summary>
        /// Initializes the pipe stream with the specified input and output streams.
        /// </summary>
        /// <param name="pipeIn">Input stream</param>
        /// <param name="pipeOut">Output stream</param>
        /// <param name="onNewClient">Set server side to get an action to use as an event that fires when a new client connects. The action will return the new client connected.</param>
        public static void Initialize(Stream pipeIn, Stream pipeOut, Action<PipeStream>? onNewClient = null)
        {
            OnNewClient = onNewClient;
            PipeIn = pipeIn;
            PipeOut = pipeOut;
            ThreadReader = new Thread(() => StartDataReader());
            ThreadReader.Start();
        }
        public static bool IsInitialized => PipeIn != null && PipeOut != null;
        static Action<PipeStream>? OnNewClient;
        public static bool AcceptNewClient = true;
        internal static readonly Dictionary<uint, PipeStream> _clientList = new Dictionary<uint, PipeStream>();
        static private Thread ThreadReader;
        /// <summary>
        /// Starts the data reader thread to handle incoming data.
        /// </summary>
        static private async void StartDataReader()
        {
            while (true)
            {
                // [0] = dataType 0=connection, 1=data
                // [1-4] = dataLength
                // [5-] = data
                var startBytes = new byte[5];
                await PipeIn.ReadAsync(startBytes, 0, 5);
                var dataType = startBytes[0];
                var dataLength = BitConverter.ToInt32(startBytes, 1);
                var data = new byte[dataLength];
                await PipeIn.ReadAsync(data, 0, dataLength);
                if (dataType == 0) // new client connection
                {
                    if (AcceptNewClient)
                    {
                        uint clientID = BitConverter.ToUInt32(data, 0);
                        var newPipeStream = new PipeStream(clientID);
                        _clientList.Add(clientID, newPipeStream);
                        OnNewClient?.Invoke(newPipeStream);
                    }
                }
                else if (dataType == 1) // data for client
                {
                    lock (_clientList)
                    {
                        if (_clientList.TryGetValue(BitConverter.ToUInt32(data, 0), out var server))
                        {
                            server.AddDataToRead(data.Skip(4).ToArray());
                        }
                    }
                }
            }
        }

        static internal Stream? PipeIn;
        static internal Stream? PipeOut;

        private Queue<byte[]> _readQueue = new Queue<byte[]>();
        private bool _disposed = false;

        public PipeStream(uint id)
        {
            Id = id;
        }
        /// <summary>
        /// Starts accepting client connections.
        /// </summary>
        public static void Start()
        {
            AcceptNewClient = true;
        }

        /// <summary>
        /// Stops accepting client connections and disposes all server streams.
        /// </summary>
        public static void Stop()
        {
            AcceptNewClient = false;
            foreach (var client in _clientList.Values)
            {
                client.Dispose();
            }
            _clientList.Clear();
        }

        public readonly uint Id;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <summary>
        /// Writes data to the pipe stream.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            var type = new byte[] { 1 };
            var dataLength = BitConverter.GetBytes(count);
            var data = buffer.Skip(offset).Take(count);
            var toSend = type.Concat(dataLength).Concat(data).ToArray();
            PipeOut.Write(toSend, 0, toSend.Length);
        }

        /// <summary>
        /// Asynchronously writes data to the pipe stream.
        /// </summary>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                lock (_readQueue)
                {
                    Write(buffer, offset, count);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Reads data from the pipe stream.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_readQueue)
            {
                if (_readQueue.Count == 0)
                    return 0;

                byte[] data = _readQueue.Dequeue();
                int bytesRead = Math.Min(data.Length, count);
                Buffer.BlockCopy(data, 0, buffer, offset, bytesRead);
                return bytesRead;
            }
        }

        /// <summary>
        /// Asynchronously reads data from the pipe stream.
        /// </summary>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                lock (_readQueue)
                {
                    return Read(buffer, offset, count);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Adds data to the read queue.
        /// </summary>
        internal void AddDataToRead(byte[] data)
        {
            lock (_readQueue)
            {
                _readQueue.Enqueue(data);
            }
        }

        /// <summary>
        /// Called when data is written asynchronously.
        /// </summary>
        protected virtual Task OnDataWrittenAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Flushes the pipe stream.
        /// </summary>
        public override void Flush()
        {
            PipeOut.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Connects to the pipe stream with a specified timeout.
        /// </summary>
        public void Connect(int timeoutMs = 0)
        {
            var type = new byte[] { 0 };
            var data = BitConverter.GetBytes(Id);
            var toSend = type.Concat(data).ToArray();
            var timeoutCancellationTokenSource = new CancellationTokenSource();

            if (timeoutMs > 0)
            {
                timeoutCancellationTokenSource.CancelAfter(timeoutMs);
            }

            try
            {
                var connectTask = Task.Run(() =>
                {
                    PipeOut.Write(toSend, 0, toSend.Length);
                    _isConnected = true;
                }, timeoutCancellationTokenSource.Token);

                connectTask.Wait(timeoutCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _isConnected = false;
                throw new TimeoutException("Connection attempt timed out.");
            }
            catch (Exception)
            {
                _isConnected = false;
                throw;
            }
        }

        private bool _isConnected = false;
        public bool IsConnected => PipeIn != null || PipeOut != null;

        /// <summary>
        /// Disposes the pipe stream.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _readQueue.Clear();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

}
