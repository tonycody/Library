using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Library.Net.Amoeba
{
    // パフォーマンス上の理由から仕方なく、これは高速化にかなり貢献してる

    sealed class CountCache : ManagerBase, IThisLock
    {
        private WatchTimer _watchTimer;
        private ConditionalWeakTable<Group, GroupManager> _table = new ConditionalWeakTable<Group, GroupManager>();
        private List<WeakReference> _groupManagers = new List<WeakReference>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public CountCache()
        {
            _watchTimer = new WatchTimer(this.WatchTimer, new TimeSpan(0, 1, 0));
        }

        private void WatchTimer()
        {
            this.Refresh();
        }

        private void Refresh()
        {
            lock (this.ThisLock)
            {
                for (int i = 0; i < _groupManagers.Count; )
                {
                    if (!_groupManagers[i].IsAlive) _groupManagers.RemoveAt(i);
                    else i++;
                }
            }
        }

        public void SetGroup(Group group)
        {
            lock (this.ThisLock)
            {
                var groupManager = new GroupManager(group);

                _table.Add(group, groupManager);
                _groupManagers.Add(new WeakReference(groupManager));
            }
        }

        public void SetState(Key key, bool state)
        {
            lock (this.ThisLock)
            {
                foreach (var weakReference in _groupManagers)
                {
                    var groupManager = weakReference.Target as GroupManager;
                    if (groupManager == null) continue;

                    groupManager.SetState(key, state);
                }
            }
        }

        public IEnumerable<Key> GetKeys(Group group, bool state)
        {
            lock (this.ThisLock)
            {
                GroupManager groupManager;
                if (!_table.TryGetValue(group, out groupManager)) throw new ArgumentException();

                return groupManager.GetKeys(state);
            }
        }

        public int GetCount(Group group)
        {
            lock (this.ThisLock)
            {
                GroupManager groupManager;
                if (!_table.TryGetValue(group, out groupManager)) throw new ArgumentException();

                return groupManager.GetCount();
            }
        }

        private class GroupManager
        {
            private Group _group;

            private SortedDictionary<Key, bool> _dic;
            //private Dictionary<Key, bool> _dic;

            private bool _isCached;
            private List<Key> _cacheTrueKeys;
            private List<Key> _cacheFalseKeys;

            public GroupManager(Group group)
            {
                _group = group;

                _dic = new SortedDictionary<Key, bool>(new Key.Comparer());
                //_dic = new Dictionary<Key, bool>();

                foreach (var key in group.Keys)
                {
                    _dic[key] = false;
                }

                _isCached = false;
                _cacheTrueKeys = new List<Key>();
                _cacheFalseKeys = new List<Key>();
            }

            public void SetState(Key key, bool state)
            {
                if (!_dic.ContainsKey(key)) return;
                _dic[key] = state;

                _isCached = false;
                _cacheTrueKeys.Clear();
                _cacheFalseKeys.Clear();
            }

            public IEnumerable<Key> GetKeys(bool state)
            {
                if (!_isCached)
                {
                    _cacheTrueKeys.AddRange(_group.Keys.Where(n => _dic[n]));
                    _cacheFalseKeys.AddRange(_group.Keys.Where(n => !_dic[n]));
                    _isCached = true;
                }

                if (state) return _cacheTrueKeys.ToList();
                else return _cacheFalseKeys.ToList();
            }

            public int GetCount()
            {
                if (!_isCached)
                {
                    _cacheTrueKeys.AddRange(_group.Keys.Where(n => _dic[n]));
                    _cacheFalseKeys.AddRange(_group.Keys.Where(n => !_dic[n]));
                    _isCached = true;
                }

                return _cacheTrueKeys.Count;
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
