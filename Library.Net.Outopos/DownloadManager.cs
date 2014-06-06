using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class DownloadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private ManagerState _state = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public Task<SectionProfileContent> Download(SectionProfileHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (header.Metadata == null) throw new ArgumentNullException("header.Metadata");

            return Task<SectionProfileContent>.Factory.StartNew(() =>
            {
                lock (this.ThisLock)
                {
                    if (!_cacheManager.Contains(header.Metadata.Key))
                    {
                        _connectionsManager.Download(header.Metadata.Key);

                        return null;
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[header.Metadata.Key];
                            return ContentConverter.FromSectionProfileContentBlock(buffer);
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

                        return null;
                    }
                }
            });
        }

        public Task<SectionMessageContent> Download(SectionMessageHeader header, ExchangePrivateKey exchangePrivateKey)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (header.Metadata == null) throw new ArgumentNullException("header.Metadata");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            return Task<SectionMessageContent>.Factory.StartNew(() =>
            {
                lock (this.ThisLock)
                {
                    if (!_cacheManager.Contains(header.Metadata.Key))
                    {
                        _connectionsManager.Download(header.Metadata.Key);

                        return null;
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[header.Metadata.Key];
                            return ContentConverter.FromSectionMessageContentBlock(buffer, exchangePrivateKey);
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

                        return null;
                    }
                }
            });
        }

        public Task<WikiPageContent> Download(WikiPageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (header.Metadata == null) throw new ArgumentNullException("header.Metadata");

            return Task<WikiPageContent>.Factory.StartNew(() =>
            {
                lock (this.ThisLock)
                {
                    if (!_cacheManager.Contains(header.Metadata.Key))
                    {
                        _connectionsManager.Download(header.Metadata.Key);

                        return null;
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[header.Metadata.Key];
                            return ContentConverter.FromWikiPageContentBlock(buffer);
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

                        return null;
                    }
                }
            });
        }

        public Task<ChatTopicContent> Download(ChatTopicHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (header.Metadata == null) throw new ArgumentNullException("header.Metadata");

            return Task<ChatTopicContent>.Factory.StartNew(() =>
            {
                lock (this.ThisLock)
                {
                    if (!_cacheManager.Contains(header.Metadata.Key))
                    {
                        _connectionsManager.Download(header.Metadata.Key);

                        return null;
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[header.Metadata.Key];
                            return ContentConverter.FromChatTopicContentBlock(buffer);
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

                        return null;
                    }
                }
            });
        }

        public Task<ChatMessageContent> Download(ChatMessageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (header.Metadata == null) throw new ArgumentNullException("header.Metadata");

            return Task<ChatMessageContent>.Factory.StartNew(() =>
            {
                lock (this.ThisLock)
                {
                    if (!_cacheManager.Contains(header.Metadata.Key))
                    {
                        _connectionsManager.Download(header.Metadata.Key);

                        return null;
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[header.Metadata.Key];
                            return ContentConverter.FromChatMessageContentBlock(buffer);
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

                        return null;
                    }
                }
            });
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
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
