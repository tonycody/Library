using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Linq;
using Library.Collections;
using Library.Security;
using Library.Io;

namespace Library.Net.Outopos
{
    class DownloadManager : ManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public Stream GetContent(Header header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(header.Key))
                {
                    _connectionsManager.Download(header.Key);
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[header.Key];
                        if (buffer.Count > 1024 * 1024 * 8) throw new DownloadManagerException();

                        var bufferStream = new BufferStream(_bufferManager);
                        bufferStream.Write(buffer.Array, buffer.Offset, buffer.Count);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        return bufferStream;
                    }
                    catch (Exception)
                    {

                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }
            }

            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }

    [Serializable]
    class DownloadManagerException : ManagerException
    {
        public DownloadManagerException() : base() { }
        public DownloadManagerException(string message) : base(message) { }
        public DownloadManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
