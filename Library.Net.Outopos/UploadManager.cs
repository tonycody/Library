using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    class UploadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private LockedHashSet<Task> _taskHashSet = new LockedHashSet<Task>();

        private volatile ManagerState _state = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;
        private const int _blockLength = 1024 * 256;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public void Upload(Tag tag, DigitalSignature digitalSignature, Stream stream)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;

                BufferStream bufferStream = null;

                try
                {
                    bufferStream = new BufferStream(_bufferManager);

                    using (var inStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position, true))
                    {
                        byte[] buffer = _bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            int length = 0;

                            while (0 != (length = inStream.Read(buffer, 0, buffer.Length)))
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            _bufferManager.ReturnBuffer(buffer);
                        }
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                }
                catch (Exception)
                {
                    if (bufferStream != null)
                    {
                        bufferStream.Dispose();
                    }

                    throw;
                }

                var item = new UploadItem();
                item.Tag = tag;
                item.CreationTime = DateTime.UtcNow;
                item.DigitalSignature = digitalSignature;
                item.Stream = bufferStream;

                var task = new Task(new Action<object>(this.UploadThread), item);
                task.ContinueWith(_ => _taskHashSet.Remove(task));

                _taskHashSet.Add(task);

                task.Start();
            }
        }

        private void UploadThread(object state)
        {
            var item = state as UploadItem;
            if (item == null) return;

            KeyCollection keys;

            {
                var headerStream = new BufferStream(_bufferManager);

                // Version
                headerStream.Write(NetworkConverter.GetBytes(0), 0, 4);

                // Type
                headerStream.WriteByte((byte)1);

                using (var dataStream = new UniteStream(headerStream, item.Stream))
                {
                    keys = _cacheManager.Encoding(dataStream, _blockLength, _hashAlgorithm);

                    foreach (var key in keys)
                    {
                        _connectionsManager.Upload(key);
                    }
                }
            }

            for (; ; )
            {
                if (keys.Count == 1)
                {
                    var header = new Header(item.Tag, item.CreationTime, keys[0], item.DigitalSignature);
                    _connectionsManager.Upload(header);
                }
                else
                {
                    var headerStream = new BufferStream(_bufferManager);

                    // Version
                    headerStream.Write(NetworkConverter.GetBytes(0), 0, 4);

                    // Type
                    headerStream.WriteByte((byte)0);

                    using (var stream = ContentConverter.CollectionToStream(keys))
                    using (var dataStream = new UniteStream(headerStream, stream))
                    {
                        keys = _cacheManager.Encoding(stream, _blockLength, _hashAlgorithm);

                        foreach (var key in keys)
                        {
                            _connectionsManager.Upload(key);
                        }
                    }
                }
            }
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;
                }
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }

                Task.WaitAll(_taskHashSet.ToArray());
            }
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
}
