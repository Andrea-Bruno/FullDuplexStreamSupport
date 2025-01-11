using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FullDuplexStreamSupport
{
    /// <summary>
    /// Adapter to convert PipeStreamClient to Stream.
    /// </summary>
    public class PipeStreamClientToStreamAdapter : Stream
    {
        private readonly PipeStreamClient _pipeStreamClient;

        public PipeStreamClientToStreamAdapter(PipeStreamClient pipeStreamClient)
        {
            _pipeStreamClient = pipeStreamClient ?? throw new ArgumentNullException(nameof(pipeStreamClient));
        }

        public override bool CanRead => _pipeStreamClient.CanRead;

        public override bool CanSeek => _pipeStreamClient.CanSeek;

        public override bool CanWrite => _pipeStreamClient.CanWrite;

        public override long Length => _pipeStreamClient.Length;

        public override long Position
        {
            get => _pipeStreamClient.Position;
            set => _pipeStreamClient.Position = value;
        }

        public override void Flush()
        {
            _pipeStreamClient.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _pipeStreamClient.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _pipeStreamClient.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _pipeStreamClient.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _pipeStreamClient.Write(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return await _pipeStreamClient.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _pipeStreamClient.WriteAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pipeStreamClient.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}