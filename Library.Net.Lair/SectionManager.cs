using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Library.Net.Caps;
using Library.Net.Connections;
using Library.Net.Proxy;
using Library;
using Library.Net.Lair;
using System.Threading;

namespace Library.Net.Lair
{
    public class SectionManager : ManagerBase, IThisLock
    {
        private Tag _tag;
        private string _leaderSignature;

        private LairManager _lairManager;
        private BufferManager _bufferManager;

        private System.Threading.Timer _watchTimer;

        private List<SectionProfileInfo> _sectionProfileInfos = new List<SectionProfileInfo>();

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        internal SectionManager(Tag tag, string leaderSignature, LairManager lairManager, BufferManager bufferManager)
        {
            _tag = tag;
            _leaderSignature = leaderSignature;

            _lairManager = lairManager;
            _bufferManager = bufferManager;

            _watchTimer = new Timer(this.WatchTimer, null, new TimeSpan(0, 0, 0), new TimeSpan(0, 1, 0));
        }

        public Tag Tag
        {
            get
            {
                return _tag;
            }
        }

        public string LeaderSignature
        {
            get
            {
                return _leaderSignature;
            }
        }

        private void WatchTimer(object state)
        {
            lock (this.ThisLock)
            {
                var link = new Link(_tag, "Section", null);
                var headers = new Dictionary<string, Header>();

                foreach (var item in _lairManager.GetHeaders(link))
                {
                    if (item.Type == "Profile")
                    {
                        headers[item.Certificate.ToString()] = item;
                    }
                }

                var infos = new List<SectionProfileInfo>();

                var checkedSignatures = new HashSet<string>();
                var checkingSignatures = new Queue<string>();

                checkingSignatures.Enqueue(_leaderSignature);

                while (checkingSignatures.Count != 0)
                {
                    var targetSignature = checkingSignatures.Dequeue();
                    if (targetSignature == null || checkedSignatures.Contains(targetSignature)) continue;

                    Header header;

                    if (headers.TryGetValue(targetSignature, out header))
                    {
                        var binaryContent = _lairManager.Download(header);
                        if (binaryContent.HasValue == false) continue;

                        var content = ContentConverter.FromSectionProfileContentBlock(binaryContent.Value);
                        if (content == null) continue;

                        foreach (var trustSignature in content.TrustSignatures)
                        {
                            checkingSignatures.Enqueue(trustSignature);
                        }

                        var info = new SectionProfileInfo();
                        info.Header = header;
                        info.Content = content;

                        infos.Add(info);
                    }

                    checkedSignatures.Add(targetSignature);
                }

                _sectionProfileInfos.Clear();
                _sectionProfileInfos.AddRange(infos);
            }
        }

        public IEnumerable<SectionProfileInfo> GetSectionProfile()
        {
            lock (this.ThisLock)
            {
                return _sectionProfileInfos.ToArray();
            }
        }

        public ChatTopicInfo GetChatTopic(string path)
        {
            lock (this.ThisLock)
            {
                var trustSigantures = new HashSet<string>(this.GetSectionProfile().SelectMany(n => n.Content.TrustSignatures));

                var link = new Link(_tag, "Chat", null);
                var headers = new List<Header>();

                foreach (var item in _lairManager.GetHeaders(link))
                {
                    if (!trustSigantures.Contains(item.Certificate.ToString())) continue;

                    if (item.Type == "Topic")
                    {
                        headers.Add(item);
                    }
                }

                headers.Sort((x, y) =>
                {
                    return y.CreationTime.CompareTo(x.CreationTime);
                });

                var lastHeader = headers.FirstOrDefault();
                if (lastHeader == null) return null;

                var binaryContent = _lairManager.Download(lastHeader);
                if (binaryContent.HasValue == false) return null;

                var content = ContentConverter.FromChatTopicContentBlock(binaryContent.Value);
                if (content == null) return null;

                var info = new ChatTopicInfo();
                info.Header = lastHeader;
                info.Content = content;

                return info;
            }
        }

        public IEnumerable<ChatMessage> GetChatMessage(string path)
        {
            lock (this.ThisLock)
            {
                var trustSigantures = new HashSet<string>(this.GetSectionProfile().SelectMany(n => n.Content.TrustSignatures));

                var link = new Link(_tag, "Chat", null);
                var headers = new List<Header>();

                foreach (var item in _lairManager.GetHeaders(link))
                {
                    if (!trustSigantures.Contains(item.Certificate.ToString())) continue;

                    if (item.Type == "Message")
                    {
                        headers.Add(item);
                    }
                }

                headers.Sort((x, y) =>
                {
                    return y.CreationTime.CompareTo(x.CreationTime);
                });

                var infos = new List<ChatMessage>();

                foreach (var header in headers.Take(1024))
                {
                    var binaryContent = _lairManager.Download(header);
                    if (binaryContent.HasValue == false) return null;

                    var content = ContentConverter.FromChatMessageContentBlock(binaryContent.Value);
                    if (content == null) return null;

                    var info = new ChatMessage();
                    info.Header = header;
                    info.Content = content;

                    infos.Add(info);
                }

                return infos;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_watchTimer != null)
                {
                    try
                    {
                        _watchTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _watchTimer = null;
                }
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
