using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Library.Net.Amoeba
{
    // パフォーマンス上の理由から仕方なく、これは高速化にかなり貢献してる

    sealed class ExistManager : ManagerBase, IThisLock
    {
        private Dictionary<Group, GroupManager> _table = new Dictionary<Group, GroupManager>(new ReferenceEqualityComparer());

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public ExistManager()
        {

        }

        public void Add(Group group)
        {
            lock (this.ThisLock)
            {
                _table[group] = new GroupManager(group);
            }
        }

        public void Remove(Group group)
        {
            lock (this.ThisLock)
            {
                _table.Remove(group);
            }
        }

        public void Set(Group group, IEnumerable<Key> keys)
        {
            lock (this.ThisLock)
            {
                GroupManager groupManager;
                if (!_table.TryGetValue(group, out groupManager)) throw new ArgumentException();

                foreach (var key in keys)
                {
                    groupManager.Set(key, true);
                }
            }
        }

        public void Set(Key key, bool state)
        {
            lock (this.ThisLock)
            {
                foreach (var groupManager in _table.Values)
                {
                    groupManager.Set(key, state);
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

            private BitArray _bitmap;

            private Dictionary<Key, bool> _dic;

            private bool _isCached;
            private List<Key> _cacheTrueKeys;
            private List<Key> _cacheFalseKeys;

            private const int BitmapSize = 2048;

            public GroupManager(Group group)
            {
                _group = group;

                _bitmap = new BitArray(GroupManager.BitmapSize);

                foreach (var key in group.Keys)
                {
                    _bitmap.Set((key.GetHashCode() & 0x7FFFFFFF) % GroupManager.BitmapSize, true);
                }

                _dic = new Dictionary<Key, bool>();

                foreach (var key in group.Keys)
                {
                    _dic[key] = false;
                }

                _isCached = false;
                _cacheTrueKeys = new List<Key>();
                _cacheFalseKeys = new List<Key>();
            }

            public void Set(Key key, bool state)
            {
                if (!_bitmap.Get((key.GetHashCode() & 0x7FFFFFFF) % GroupManager.BitmapSize)) return;

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
                    foreach (var key in _group.Keys)
                    {
                        bool flag = _dic[key];

                        if (flag)
                        {
                            _cacheTrueKeys.Add(key);
                        }
                        else
                        {
                            _cacheFalseKeys.Add(key);
                        }
                    }

                    _isCached = true;
                }

                if (state) return _cacheTrueKeys.ToList();
                else return _cacheFalseKeys.ToList();
            }

            public int GetCount()
            {
                if (!_isCached)
                {
                    foreach (var key in _group.Keys)
                    {
                        bool flag = _dic[key];

                        if (flag)
                        {
                            _cacheTrueKeys.Add(key);
                        }
                        else
                        {
                            _cacheFalseKeys.Add(key);
                        }
                    }

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
