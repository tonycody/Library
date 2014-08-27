using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class DownloadManager : ManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public IEnumerable<Profile> GetProfiles()
        {
            lock (this.ThisLock)
            {
                var list = new List<Profile>();

                foreach (var signature in _connectionsManager.TrustSignatures)
                {
                    var metadata = _connectionsManager.GetProfileMetadata(signature);

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            list.Add(InformationConverter.FromProfileBlock(buffer));
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

                return list;
            }
        }

        public IEnumerable<SignatureMessage> GetSignatureMessages(string signature, int limit, ExchangePrivateKey exchangePrivateKey)
        {
            if (signature == null) throw new ArgumentNullException("signature");
            if (!Signature.Check(signature)) throw new ArgumentException("signature");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                var list = new List<SignatureMessage>();

                foreach (var metadata in _connectionsManager.GetSignatureMessageMetadatas(signature))
                {
                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            list.Add(InformationConverter.FromSignatureMessageBlock(buffer, exchangePrivateKey));
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

                return list;
            }
        }

        public IEnumerable<WikiPage> GetWikiPages(Wiki tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                var list = new List<WikiPage>();

                foreach (var metadata in _connectionsManager.GetWikiPageMetadatas(tag))
                {
                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            list.Add(InformationConverter.FromWikiPageBlock(buffer));
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

                return list;
            }
        }

        public IEnumerable<ChatTopic> GetChatTopics(Chat tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                var list = new List<ChatTopic>();

                foreach (var metadata in _connectionsManager.GetChatTopicMetadatas(tag))
                {
                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            list.Add(InformationConverter.FromChatTopicBlock(buffer));
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

                return list;
            }
        }

        public IEnumerable<ChatMessage> GetChatMessages(Chat tag)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                var list = new List<ChatMessage>();

                foreach (var metadata in _connectionsManager.GetChatMessageMetadatas(tag))
                {
                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            list.Add(InformationConverter.FromChatMessageBlock(buffer));
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

                return list;
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
