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
    class DownloadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private VolatileHashDictionary<Header, DownloadItem> _downloadItems;

        private volatile Thread _downloadManagerThread;

        private volatile ManagerState _state = ManagerState.Stop;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _downloadItems = new VolatileHashDictionary<Header, DownloadItem>(new TimeSpan(0, 12, 0));
        }

        private void DownloadThread()
        {
            Stopwatch refreshStopwatch = new Stopwatch();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                _downloadItems.TrimExcess();

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalSeconds >= 20)
                {
                    refreshStopwatch.Restart();

                    foreach (var pair in _downloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            {
                                bool flag = false;

                                foreach (var key in item.Keys.ToArray())
                                {
                                    if (_cacheManager.Contains(key)) continue;
                                    _connectionsManager.Download(key);

                                    flag = true;
                                }

                                if (flag) continue;
                            }

                            {
                                BufferStream bufferStream = null;

                                try
                                {
                                    bufferStream = new BufferStream(_bufferManager);

                                    using (var wrapperStream = new WrapperStream(bufferStream, true))
                                    {
                                        _cacheManager.Decoding(wrapperStream, item.Keys);
                                    }

                                    bufferStream.Seek(0, SeekOrigin.Begin);

                                    int version;
                                    {
                                        byte[] versionBuffer = new byte[4];
                                        if (bufferStream.Read(versionBuffer, 0, versionBuffer.Length) != versionBuffer.Length) return;
                                        version = NetworkConverter.ToInt32(versionBuffer);
                                    }

                                    if (version == 0)
                                    {
                                        byte type;
                                        {
                                            type = (byte)bufferStream.ReadByte();
                                        }

                                        if (type == 0)
                                        {
                                            var tempKeys = ContentConverter.StreamToCollection<Key>(bufferStream);
                                            bufferStream.Dispose();

                                            lock (this.ThisLock)
                                            {
                                                item.Keys.Clear();
                                                item.Keys.AddRange(tempKeys);
                                            }

                                            continue;
                                        }
                                        else if (type == 1)
                                        {
                                            item.Stream = new RangeStream(bufferStream, bufferStream.Position, bufferStream.Length - bufferStream.Position);
                                        }
                                        else
                                        {
                                            throw new NotSupportedException();
                                        }
                                    }
                                    else
                                    {
                                        throw new NotSupportedException();
                                    }
                                }
                                catch (Exception)
                                {
                                    if (bufferStream != null)
                                    {
                                        bufferStream.Dispose();
                                    }
                                }
                            }

                            item.Keys.Clear();
                            item.State = DownloadState.Completed;
                        }
                        catch (Exception e)
                        {
                            item.Keys.Clear();
                            item.State = DownloadState.Error;

                            Log.Error(e);
                        }
                    }
                }
            }
        }

        public Stream Download(Header header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return null;

                DownloadItem item;

                if (!_downloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    item.Keys.Add(header.Key);
                    item.State = DownloadState.Downloading;

                    _downloadItems[header] = item;
                }

                return item.Stream;
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
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _downloadManagerThread = new Thread(this.DownloadThread);
                    _downloadManagerThread.Priority = ThreadPriority.Lowest;
                    _downloadManagerThread.Name = "DownloadManager_DownloadThread";
                    _downloadManagerThread.Start();
                }
            }
        }

        public override void Stop()
        {
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }

                _downloadManagerThread.Join();
                _downloadManagerThread = null;
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

    [Serializable]
    class DownloadManagerException : ManagerException
    {
        public DownloadManagerException() : base() { }
        public DownloadManagerException(string message) : base(message) { }
        public DownloadManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class DecodeException : DownloadManagerException
    {
        public DecodeException() : base() { }
        public DecodeException(string message) : base(message) { }
        public DecodeException(string message, Exception innerException) : base(message, innerException) { }
    }
}
