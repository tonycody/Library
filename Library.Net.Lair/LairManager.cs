using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Lair
{
    public sealed class LairManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _cachePath;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private ConnectionsManager _connectionsManager;

        private ManagerState _state = ManagerState.Stop;

        private TrustSignaturesEventHandler _trustSignaturesEvent;
        private LockDocumentsEventHandler _lockDocumentsEvent;
        private LockChatsEventHandler _lockChatsEvent;
        private LockWhispersEventHandler _lockWhispersEvent;
        private LockMailsEventHandler _lockMailsEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public LairManager(string cachePath, BufferManager bufferManager)
        {
            _cachePath = cachePath;

            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _cacheManager = new CacheManager(_cachePath, _bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _cacheManager, _bufferManager);

            _connectionsManager.TrustSignaturesEvent = (object sender) =>
            {
                if (_trustSignaturesEvent != null)
                {
                    return _trustSignaturesEvent(this);
                }

                return null;
            };

            _connectionsManager.LockDocumentsEvent = (object sender) =>
            {
                if (_lockDocumentsEvent != null)
                {
                    return _lockDocumentsEvent(this);
                }

                return null;
            };

            _connectionsManager.LockChatsEvent = (object sender) =>
            {
                if (_lockChatsEvent != null)
                {
                    return _lockChatsEvent(this);
                }

                return null;
            };

            _connectionsManager.LockWhispersEvent = (object sender) =>
            {
                if (_lockWhispersEvent != null)
                {
                    return _lockWhispersEvent(this);
                }

                return null;
            };

            _connectionsManager.LockMailsEvent = (object sender) =>
            {
                if (_lockMailsEvent != null)
                {
                    return _lockMailsEvent(this);
                }

                return null;
            };
        }

        public TrustSignaturesEventHandler TrustSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _trustSignaturesEvent = value;
                }
            }
        }

        public LockDocumentsEventHandler LockDocumentsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockDocumentsEvent = value;
                }
            }
        }

        public LockChatsEventHandler LockChatsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockChatsEvent = value;
                }
            }
        }

        public LockWhispersEventHandler LockWhispersEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockWhispersEvent = value;
                }
            }
        }

        public LockMailsEventHandler LockMailsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockMailsEvent = value;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();
                    contexts.AddRange(_connectionsManager.Information);
                    contexts.AddRange(_cacheManager.Information);

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.BaseNode;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.BaseNode = value;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _clientManager.Filters;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _serverManager.ListenUris;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public int BandWidthLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.BandWidthLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.BandWidthLimit = value;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public long Size
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _cacheManager.Size;
                }
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void Resize(long size)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _cacheManager.Resize(size);
            }
        }

        public void CheckBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _cacheManager.CheckBlocks(getProgressEvent);
        }

        public void SendSignatureRequest(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendSignatureRequest(signature);
            }
        }

        public void SendDocumentRequest(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendDocumentRequest(document);
            }
        }

        public void SendChatRequest(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendChatRequest(chat);
            }
        }

        public void SendWhisperRequest(Whisper whisper)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendWhisperRequest(whisper);
            }
        }

        public void SendMailRequest(Mail mail)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendMailRequest(mail);
            }
        }

        public IEnumerable<string> GetSignatures()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSignatures();
            }
        }

        public IEnumerable<Document> GetDocuments()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetDocuments();
            }
        }

        public IEnumerable<Chat> GetChats()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChats();
            }
        }

        public IEnumerable<Whisper> GetWhispers()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetWhispers();
            }
        }

        public IEnumerable<Mail> GetMails()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMails();
            }
        }

        public SignatureProfile GetSignatureProfile(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSignatureProfile(signature);
            }
        }

        public IEnumerable<DocumentArchive> GetDocumentSites(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetDocumentSites(document);
            }
        }

        public IEnumerable<DocumentOpinionContent> GetDocumentOpinions(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetDocumentOpinions(document);
            }
        }

        public IEnumerable<ChatTopicContent> GetChatTopics(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChatTopics(chat);
            }
        }

        public IEnumerable<ChatMessageContent> GetChatMessages(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChatMessages(chat);
            }
        }

        public IEnumerable<WhisperMessage> GetWhisperMessages(Whisper whisper)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetWhisperMessages(whisper);
            }
        }

        public IEnumerable<MailMessage> GetMailMessages(Mail mail)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMailMessages(mail);
            }
        }

        public SignatureProfileContent GetContent(SignatureProfile signatureProfile)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(signatureProfile);
            }
        }

        public DocumentArchive GetContent(DocumentArchive documentSite)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(documentSite);
            }
        }

        public DocumentOpinionContent GetContent(DocumentOpinionContent documentOpinion)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(documentOpinion);
            }
        }

        public ChatTopicContent GetContent(ChatTopicContent chatTopic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(chatTopic);
            }
        }

        public ChatMessageContent GetContent(ChatMessageContent chatMessage)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(chatMessage);
            }
        }

        public WhisperMessageContent GetContent(WhisperMessage whisperMessage, WhisperCryptoInformation cryptoInformation)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(whisperMessage, cryptoInformation);
            }
        }

        public MailMessage GetContent(MailMessage mailMessage, IExchangeDecrypt exchangeDecrypt)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(mailMessage, exchangeDecrypt);
            }
        }

        public void UploadSignatureProfile(string comment, IExchangeEncrypt exchangeEncrypt, IEnumerable<string> trustSignatures, IEnumerable<Document> documents, IEnumerable<Chat> chats, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadSignatureProfile(comment, exchangeEncrypt, trustSignatures, documents, chats, digitalSignature);
            }
        }

        public void UploadDocumentSite(Document document, 
            IEnumerable<DocumentPageContent> documentPages, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadDocumentSite(document, documentPages, digitalSignature);
            }
        }

        public void UploadDocumentOpinion(Document document,
             IEnumerable<Key> goods, IEnumerable<Key> bads, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadDocumentOpinion(document, goods, bads, digitalSignature);
            }
        }

        public void UploadChatTopic(Chat chat,
            string comment, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadChatTopic(chat, comment, digitalSignature);
            }
        }

        public void UploadChatMessage(Chat chat,
            string comment, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadChatMessage(chat, comment, anchors, digitalSignature);
            }
        }

        public void UploadWhisperMessage(Whisper whisper,
            string comment, IEnumerable<Key> anchors, WhisperCryptoInformation cryptoInformation, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadWhisperMessage(whisper, comment, anchors, cryptoInformation, digitalSignature);
            }
        }

        public void UploadMailMessage(Mail mail,
            string text, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadMailMessage(mail, text, exchangeEncrypt, digitalSignature);
            }
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

        public override void Start()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _connectionsManager.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _connectionsManager.Stop();
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                this.Stop();

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _serverManager.Save(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _clientManager.Save(System.IO.Path.Combine(directoryPath, "ClientManager"));
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _connectionsManager.Dispose();
                _serverManager.Dispose();
                _clientManager.Dispose();
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
