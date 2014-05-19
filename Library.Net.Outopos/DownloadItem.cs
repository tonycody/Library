using System.Runtime.Serialization;
using System;
using Library.Security;
using System.IO;

namespace Library.Net.Outopos
{
    enum DownloadState
    {
        Downloading = 0,
        Completed = 1,
        Error = 2,
    }

    sealed class DownloadItem
    {
        private DownloadState _state;
        private KeyCollection _keys;
        private Stream _stream;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        private object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        public DownloadState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _state = value;
                }
            }
        }

        public KeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _keys;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _keys = value;
                }
            }
        }

        public Stream Stream
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stream;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _stream = value;
                }
            }
        }
    }
}
