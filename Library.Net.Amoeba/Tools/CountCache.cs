using System.Collections.Generic;
using System.Linq;

namespace Library.Net.Amoeba
{
    // パフォーマンス上の理由から仕方なく、これは高速化にかなり貢献してる

    sealed class CountCache
    {
        private Dictionary<Group, GroupManager> _groupManagers = new Dictionary<Group, GroupManager>();

        private readonly object _thisLock = new object();

        public void SetGroup(Group group)
        {
            lock (this.ThisLock)
            {
                _groupManagers[group] = new GroupManager(group);
            }
        }

        public void SetState(Key key, bool state)
        {
            lock (this.ThisLock)
            {
                foreach (var m in _groupManagers.Values)
                {
                    m.SetState(key, state);
                }
            }
        }

        public IEnumerable<Key> GetKeys(Group group, bool state)
        {
            lock (this.ThisLock)
            {
                GroupManager groupManager;

                if (_groupManagers.TryGetValue(group, out groupManager))
                {
                    return groupManager.GetKeys(state);
                }

                return new Key[0];
            }
        }

        public int GetCount(Group group)
        {
            lock (this.ThisLock)
            {
                GroupManager groupManager;

                if (_groupManagers.TryGetValue(group, out groupManager))
                {
                    return groupManager.GetCount(true);
                }

                return 0;
            }
        }

        private class GroupManager
        {
            private Group _group;

            //private SortedList<Key, bool> _dic;
            private Dictionary<Key, bool> _dic;

            private bool _isCached;
            private List<Key> _cacheTrueKeys;
            private List<Key> _cacheFalseKeys;

            public GroupManager(Group group)
            {
                _group = group;

                //_dic = new SortedList<Key, bool>(new KeyComparer());
                _dic = new Dictionary<Key, bool>();

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

                if (state) return _cacheTrueKeys;
                else return _cacheFalseKeys;
            }

            public int GetCount(bool state)
            {
                if (!_isCached)
                {
                    _cacheTrueKeys.AddRange(_group.Keys.Where(n => _dic[n]));
                    _cacheFalseKeys.AddRange(_group.Keys.Where(n => !_dic[n]));
                    _isCached = true;
                }

                if (state) return _cacheTrueKeys.Count;
                else return _cacheFalseKeys.Count;
            }

            //class KeyComparer : IComparer<Key>
            //{
            //    public int Compare(Key x, Key y)
            //    {
            //        int c = x.HashAlgorithm.CompareTo(y.HashAlgorithm);
            //        if (c != 0) return c;

            //        // Unsafe
            //        if (Unsafe.Equals(x.Hash, y.Hash)) return 0;

            //        c = Collection.Compare(x.Hash, y.Hash);
            //        if (c != 0) return c;

            //        return 0;
            //    }
            //}
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
