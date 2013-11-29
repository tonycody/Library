using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using System.Diagnostics;

namespace Library.Net.Lair
{
    public delegate void DownloadSectionProfileEventHandler(object sender, Header header, SectionProfileContent content);

    class DownloadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private LockedDictionary<byte[], Key> _hashToKey = new LockedDictionary<byte[], Key>(new ByteArrayEqualityComparer());
        private LockedQueue<SectionProfileInfo> _sectionProfileInfoQueue = new LockedQueue<SectionProfileInfo>();

        private volatile Thread _downloadManagerThread;

        private ManagerState _state = ManagerState.Stop;
        private ManagerState _decodeState = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private event DownloadSectionProfileEventHandler _downloadSectionProfileEvent;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);
        }

        public event DownloadSectionProfileEventHandler DownloadSectionProfileEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _downloadSectionProfileEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _downloadSectionProfileEvent -= value;
                }
            }
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();

                    //contexts.Add(new InformationContext("DownloadingCount", _settings.DownloadItems
                    //    .Count(n => !(n.State == DownloadState.Completed || n.State == DownloadState.Error))));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<Information> list = new List<Information>();

                    //foreach (var item in _ids)
                    //{
                    //    List<InformationContext> contexts = new List<InformationContext>();

                    //    contexts.Add(new InformationContext("Id", item.Key));

                    //    list.Add(new Information(contexts));
                    //}

                    return list;
                }
            }
        }

        public IEnumerable<Header> Headers
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Headers;
                }
            }
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    return text;
                }
            }
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    FileStream fs = new FileStream(path, FileMode.CreateNew);
                    return fs;
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {
                    throw;
                }
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        FileStream fs = new FileStream(text, FileMode.CreateNew);
                        return fs;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        if (index > 100) throw;
                    }
                }
            }
        }

        private static string GetNormalizedPath(string path)
        {
            string filePath = path;

            foreach (char ic in Path.GetInvalidFileNameChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }
            foreach (char ic in Path.GetInvalidPathChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }

            return filePath;
        }

        private void DownloadManagerThread()
        {
            Stopwatch refreshStopwatch = new Stopwatch();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    refreshStopwatch.Restart();

                    foreach (var header in _settings.Headers.ToArray())
                    {
                        if (header.FormatType == ContentFormatType.Raw)
                        {

                        }
                    }
                }
            }
        }

        protected virtual void OnDownloadSectionProfileEvent(Header header, SectionProfileContent content)
        {
            if (_downloadSectionProfileEvent != null)
            {
                _downloadSectionProfileEvent(this, header, content);
            }
        }

        public void Download(Header header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                _settings.Headers.Add(header);
            }
        }

        public void Remove(Header header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                _settings.Headers.Remove(header);
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
            while (_downloadManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _downloadManagerThread = new Thread(this.DownloadManagerThread);
                _downloadManagerThread.Priority = ThreadPriority.Lowest;
                _downloadManagerThread.Name = "DownloadManager_DownloadManagerThread";
                _downloadManagerThread.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _downloadManagerThread.Join();
            _downloadManagerThread = null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);
            }
        }

        public void Save(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedHashSet<Header>>() { Name = "Headers", Value = new LockedHashSet<Header>() },
                })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public LockedHashSet<Header> Headers
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Header>)this["Headers"];
                    }
                }
            }
        }

        private class SectionProfileInfo
        {
            public Header Header { get; set; }
            public SectionProfileContent Content { get; set; }
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
}
