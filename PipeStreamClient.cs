using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices;

namespace FullDuplexStreamSupport
{
    /// <summary>
    /// Represents a client connected to a pipe stream.
    /// </summary>
    public class PipeStreamClient : Stream, IStream
    {
        // private Queue<byte[]> _readQueue = new Queue<byte[]>();
        private AutoResetEvent _dataAvailable = new AutoResetEvent(false);
        private List<byte> _readQueue = new List<byte>();
        private bool _disposed = false;
        PipeStream PipeStream;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStreamClient"/> class.
        /// </summary>
        /// <param name="pipeStream">The pipe stream to connect to.</param>
        /// <param name="clientId">The client ID.</param>
        public PipeStreamClient(PipeStream pipeStream, uint? clientId = null)
        {
            lock (pipeStream)
            {
                if (!pipeStream.PipeisConnected)
                {
#if DEBUG && !TEST

                    Debugger.Break();
#endif
                    throw new InvalidOperationException("Pipe is not connected.");
                }
                PipeStream = pipeStream;
                if (clientId != null)
                {
                    _isConnected = true;
                    PipeStream.NextClientId = (uint)clientId;
                }
                Id = PipeStream.NextClientId;
                PipeStream.NextClientId++;
            }
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
            var pipeOut = PipeStream.PipeOut;
            if (pipeOut == null)
            {
                Debugger.Break(); // PipeOut is null, investigate why
                return;
            }
            lock (pipeOut)
            {
                var type = new byte[] { (byte)PipeStream.DataType.DataTransmission };
                var clientId = BitConverter.GetBytes(Id);
                var dataLength = BitConverter.GetBytes(clientId.Length + count);
                var data = buffer.Skip(offset).Take(count);
                var toSend = type.Concat(dataLength).Concat(clientId).Concat(data).ToArray();
                pipeOut.Write(toSend, 0, toSend.Length);
            }
        }

        /// <summary>
        /// Asynchronously writes data to the pipe stream.
        /// </summary>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                lock (WriteLock)
                {
                    Write(buffer, offset, count);
                }
            }, cancellationToken);
        }
        private object WriteLock = new object();

        /// <summary>
        /// Reads data from the pipe stream.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            lock (ReadLock)
            {
                InReading = count;
                _dataAvailable.Reset();
                while (true)
                {
                    lock (_readQueue)
                    {
                        if (_readQueue.Count >= count)
                            break;
                        if (_disposed)
                            throw new ObjectDisposedException(nameof(PipeStreamClient));
                    }
                    _dataAvailable.WaitOne();
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(PipeStreamClient));
                }

                lock (_readQueue)
                {
                    int toCopy = Math.Min(count, _readQueue.Count);
                    _readQueue.CopyTo(0, buffer, offset, toCopy);
                    _readQueue.RemoveRange(0, toCopy);
                    bytesRead = toCopy;
                }
            }
            return bytesRead;
        }

        private object ReadLock = new object();
        private int InReading;

        /// <summary>
        /// Asynchronously reads data from the pipe stream.
        /// </summary>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                return Read(buffer, offset, count);
            }, cancellationToken);
        }

        /// <summary>
        /// Begins an asynchronous read operation.
        /// </summary>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            var task = ReadAsync(buffer, offset, count);
            var tcs = new TaskCompletionSource<int>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    tcs.TrySetException(t.Exception.InnerExceptions);
                }
                else if (t.IsCanceled)
                {
                    tcs.TrySetCanceled();
                }
                else
                {
                    tcs.TrySetResult(t.Result);
                }
                callback?.Invoke(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }

        /// <summary>
        /// Ends an asynchronous read operation.
        /// </summary>
        public override int EndRead(IAsyncResult asyncResult)
        {
            return ((Task<int>)asyncResult).Result;
        }

        /// <summary>
        /// Adds data to the read queue.
        /// </summary>
        internal void AddDataToRead(byte[] data)
        {
            lock (_readQueue)
            {
                _readQueue.AddRange(data);
                if (_readQueue.Count >= InReading)
                {
                    _dataAvailable.Set();
                }
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
            PipeStream.PipeOut?.Flush();
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
            var type = new byte[] { (byte)PipeStream.DataType.NewClient };
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
                    PipeStream.PipeOut?.Write(toSend, 0, toSend.Length);
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
        public bool IsConnected => !PipeStream.DataError && PipeStream.PipeisConnected && _isConnected && PipeStream.PipeIn != null && PipeStream.PipeOut != null;

        /// <summary>
        /// Disposes the pipe stream.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            Debugger.Break(); // Investigate about close connection
            if (!_disposed)
            {
                if (disposing)
                {
                    _dataAvailable.Set();
                    PipeStream?.PipeOut?.Close();
                    PipeStream?.PipeOut?.Dispose();
                    PipeStream?.PipeIn?.Close();
                    PipeStream?.PipeIn?.Dispose();
                    //_readQueue.Clear();
                    _dataAvailable.Dispose();
#if DEBUG && !TEST
                    Debugger.Break();
#endif
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Closes the pipe stream and releases all resources.
        /// </summary>
        public override void Close()
        {
            if (!_disposed)
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Creates a new stream object that references the same bytes as the original stream.
        /// </summary>
        /// <param name="ppstm">When this method returns, contains the new stream object.</param>
        public void Clone(out IStream ppstm)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Ensures that any changes made to a stream object open in transacted mode are reflected in the parent storage.
        /// </summary>
        /// <param name="grfCommitFlags">Specifies how the changes are committed.</param>
        public void Commit(int grfCommitFlags)
        {
            // No transactional support, so nothing to do here
        }

        /// <summary>
        /// Copies a specified number of bytes from the current seek pointer in the stream to the current seek pointer in another stream.
        /// </summary>
        /// <param name="pstm">A reference to the destination stream.</param>
        /// <param name="cb">The number of bytes to copy from the source stream.</param>
        /// <param name="pcbRead">On successful return, contains the actual number of bytes read from the source.</param>
        /// <param name="pcbWritten">On successful return, contains the actual number of bytes written to the destination.</param>
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
        {
            byte[] buffer = new byte[4096];
            long written = 0;
            while (written < cb)
            {
                int toRead = (int)Math.Min(buffer.Length, cb - written);
                int read = Read(buffer, 0, toRead);
                if (read == 0)
                    break;
                pstm.Write(buffer, read, IntPtr.Zero);
                written += read;
            }
            if (pcbRead != IntPtr.Zero)
                Marshal.WriteInt64(pcbRead, written);
            if (pcbWritten != IntPtr.Zero)
                Marshal.WriteInt64(pcbWritten, written);
        }

        /// <summary>
        /// Restricts access to a specified range of bytes in the stream.
        /// </summary>
        /// <param name="libOffset">The byte offset for the beginning of the range.</param>
        /// <param name="cb">The length of the range, in bytes, to restrict.</param>
        /// <param name="dwLockType">The requested restrictions on accessing the range.</param>
        public void LockRegion(long libOffset, long cb, int dwLockType)
        {
            // No support for locking regions
            throw new NotImplementedException();
        }

        /// <summary>
        /// Reads a specified number of bytes from the stream object into memory starting at the current seek pointer.
        /// </summary>
        /// <param name="pv">The buffer into which the stream data is read.</param>
        /// <param name="cb">The number of bytes of data to read from the stream object.</param>
        /// <param name="pcbRead">On successful return, contains the actual number of bytes read from the stream object.</param>
        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int bytesRead = Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero)
                Marshal.WriteInt32(pcbRead, bytesRead);
        }

        /// <summary>
        /// Discards all changes that have been made to a transacted stream since the last <see cref="Commit"/> call.
        /// </summary>
        public void Revert()
        {
            // No transactional support, so nothing to do here
        }

        /// <summary>
        /// Changes the seek pointer to a new location relative to the beginning of the stream, the end of the stream, or the current seek pointer.
        /// </summary>
        /// <param name="dlibMove">The displacement to add to dwOrigin.</param>
        /// <param name="dwOrigin">The origin for the displacement.</param>
        /// <param name="plibNewPosition">On successful return, contains the offset of the seek pointer from the beginning of the stream.</param>
        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long newPosition = Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, newPosition);
        }

        /// <summary>
        /// Changes the size of the stream object.
        /// </summary>
        /// <param name="libNewSize">The new size of the stream as a number of bytes.</param>
        public void SetSize(long libNewSize)
        {
            SetLength(libNewSize);
        }

        /// <summary>
        /// Retrieves the <see cref="STATSTG"/> structure for this stream.
        /// </summary>
        /// <param name="pstatstg">When this method returns, contains a <see cref="STATSTG"/> structure that describes this stream object.</param>
        /// <param name="grfStatFlag">Specifies that this method does not return some of the members in the <see cref="STATSTG"/> structure, thus saving a memory allocation operation.</param>
        public void Stat(out STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new STATSTG
            {
                type = 2, // STGTY_STREAM
                cbSize = Length,
                grfMode = 0 // Mode is not relevant for this implementation
            };
        }

        /// <summary>
        /// Removes the access restriction on a range of bytes previously restricted with the <see cref="LockRegion"/> method.
        /// </summary>
        /// <param name="libOffset">The byte offset for the beginning of the range.</param>
        /// <param name="cb">The length, in bytes, of the range to restrict.</param>
        /// <param name="dwLockType">The access restrictions previously placed on the range.</param>
        public void UnlockRegion(long libOffset, long cb, int dwLockType)
        {
            // No support for unlocking regions
            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes a specified number of bytes into the stream object starting at the current seek pointer.
        /// </summary>
        /// <param name="pv">The buffer that contains the data to write to the stream.</param>
        /// <param name="cb">The number of bytes of data to write to the stream.</param>
        /// <param name="pcbWritten">On successful return, contains the actual number of bytes written to the stream object.</param>
        public void Write(byte[] pv, int cb, IntPtr pcbWritten)
        {
            Write(pv, 0, cb);
            if (pcbWritten != IntPtr.Zero)
                Marshal.WriteInt32(pcbWritten, cb);
        }
    }
}