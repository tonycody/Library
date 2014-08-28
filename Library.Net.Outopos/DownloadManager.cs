using System;
using System.Collections.Generic;
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

                            var information = InformationConverter.FromProfileBlock(buffer);
                            if (metadata.CreationTime != information.CreationTime) continue;
                            if (metadata.Certificate.ToString() != information.Certificate.ToString()) continue;

                            list.Add(information);
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
                    if (!_connectionsManager.ContainsTrustSignature(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

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

                            var information = InformationConverter.FromSignatureMessageBlock(buffer, exchangePrivateKey);
                            if (metadata.Signature != information.Signature) continue;
                            if (metadata.CreationTime != information.CreationTime) continue;
                            if (metadata.Certificate.ToString() != information.Certificate.ToString()) continue;

                            list.Add(information);
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

        public IEnumerable<WikiDocument> GetWikiDocuments(Wiki tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                var list = new List<WikiDocument>();

                foreach (var metadata in _connectionsManager.GetWikiDocumentMetadatas(tag))
                {
                    if (!_connectionsManager.ContainsTrustSignature(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

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

                            var information = InformationConverter.FromWikiDocumentBlock(buffer);
                            if (metadata.Tag != information.Tag) continue;
                            if (metadata.CreationTime != information.CreationTime) continue;
                            if (metadata.Certificate.ToString() != information.Certificate.ToString()) continue;

                            list.Add(information);
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

        public IEnumerable<ChatTopic> GetChatTopics(Chat tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                var list = new List<ChatTopic>();

                foreach (var metadata in _connectionsManager.GetChatTopicMetadatas(tag))
                {
                    if (!_connectionsManager.ContainsTrustSignature(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

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

                            var information = InformationConverter.FromChatTopicBlock(buffer);
                            if (metadata.Tag != information.Tag) continue;
                            if (metadata.CreationTime != information.CreationTime) continue;
                            if (metadata.Certificate.ToString() != information.Certificate.ToString()) continue;

                            list.Add(information);
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

        public IEnumerable<ChatMessage> GetChatMessages(Chat tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                var list = new List<ChatMessage>();

                foreach (var metadata in _connectionsManager.GetChatMessageMetadatas(tag))
                {
                    if (!_connectionsManager.ContainsTrustSignature(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

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

                            var information = InformationConverter.FromChatMessageBlock(buffer);
                            if (metadata.Tag != information.Tag) continue;
                            if (metadata.CreationTime != information.CreationTime) continue;
                            if (metadata.Certificate.ToString() != information.Certificate.ToString()) continue;

                            list.Add(information);
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
