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

        private ConditionalWeakTable<ProfileHeader, ProfileContent> _profileTable = new ConditionalWeakTable<ProfileHeader, ProfileContent>();
        private ConditionalWeakTable<SignatureMessageHeader, SignatureMessageContent> _signatureMessageTable = new ConditionalWeakTable<SignatureMessageHeader, SignatureMessageContent>();
        private ConditionalWeakTable<WikiPageHeader, WikiPageContent> _wikiPageTable = new ConditionalWeakTable<WikiPageHeader, WikiPageContent>();
        private ConditionalWeakTable<ChatTopicHeader, ChatTopicContent> _chatTopicTable = new ConditionalWeakTable<ChatTopicHeader, ChatTopicContent>();
        private ConditionalWeakTable<ChatMessageHeader, ChatMessageContent> _chatMessageTable = new ConditionalWeakTable<ChatMessageHeader, ChatMessageContent>();

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public ProfileContent GetContent(ProfileHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                {
                    ProfileContent content;

                    if (_profileTable.TryGetValue(header, out content))
                    {
                        return content;
                    }
                }

                if (!_cacheManager.Contains(header.Key))
                {
                    _connectionsManager.Download(header.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[header.Key];

                        var content = ContentConverter.FromProfileContentBlock(buffer);
                        _profileTable.Add(header, content);

                        return content;
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
        }

        public SignatureMessageContent GetContent(SignatureMessageHeader header, ExchangePrivateKey exchangePrivateKey)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                {
                    SignatureMessageContent content;

                    if (_signatureMessageTable.TryGetValue(header, out content))
                    {
                        return content;
                    }
                }

                if (!_cacheManager.Contains(header.Key))
                {
                    _connectionsManager.Download(header.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[header.Key];

                        var content = ContentConverter.FromSignatureMessageContentBlock(buffer, exchangePrivateKey);
                        _signatureMessageTable.Add(header, content);

                        return content;
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
        }

        public WikiPageContent GetContent(WikiPageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                {
                    WikiPageContent content;

                    if (_wikiPageTable.TryGetValue(header, out content))
                    {
                        return content;
                    }
                }

                if (!_cacheManager.Contains(header.Key))
                {
                    _connectionsManager.Download(header.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[header.Key];

                        var content = ContentConverter.FromWikiPageContentBlock(buffer);
                        _wikiPageTable.Add(header, content);

                        return content;
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
        }

        public ChatTopicContent GetContent(ChatTopicHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                {
                    ChatTopicContent content;

                    if (_chatTopicTable.TryGetValue(header, out content))
                    {
                        return content;
                    }
                }

                if (!_cacheManager.Contains(header.Key))
                {
                    _connectionsManager.Download(header.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[header.Key];

                        var content = ContentConverter.FromChatTopicContentBlock(buffer);
                        _chatTopicTable.Add(header, content);

                        return content;
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
        }

        public ChatMessageContent GetContent(ChatMessageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                {
                    ChatMessageContent content;

                    if (_chatMessageTable.TryGetValue(header, out content))
                    {
                        return content;
                    }
                }

                if (!_cacheManager.Contains(header.Key))
                {
                    _connectionsManager.Download(header.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[header.Key];

                        var content = ContentConverter.FromChatMessageContentBlock(buffer);
                        _chatMessageTable.Add(header, content);

                        return content;
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
