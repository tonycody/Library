using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    sealed class UploadItem
    {
        private Tag _tag;
        private DateTime _creationTime;
        private DigitalSignature _digitalSignature;
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

        public Tag Tag
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tag;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _tag = value;
                }
            }
        }

        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _creationTime = value;
                }
            }
        }

        public DigitalSignature DigitalSignature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _digitalSignature;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _digitalSignature = value;
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
