using System.Runtime.Serialization;
using System;

namespace Library.Net.Lair
{
    [DataContract(Name = "DownloadState", Namespace = "http://Library/Net/Lair")]
    public enum DownloadState
    {
        [EnumMember(Value = "Downloading")]
        Downloading = 0,

        [EnumMember(Value = "Completed")]
        Completed = 1,

        [EnumMember(Value = "Error")]
        Error = 2,
    }

    [DataContract(Name = "DownloadItem", Namespace = "http://Library/Net/Lair")]
    sealed class DownloadItem
    {
        private DownloadState _state;

        private Key _key;
        private ArraySegment<byte> _content;

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

        [DataMember(Name = "State")]
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

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _key;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _key = value;
                }
            }
        }

        [DataMember(Name = "Content")]
        public ArraySegment<byte> Content
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _content;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _content = value;
                }
            }
        }
    }
}
