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

                return new KeyCollection();
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

            private HashSet<Key> _KeyHashset = new HashSet<Key>();

            private HashSet<Key> _trueKeyHashset = new HashSet<Key>();

            private List<Key> _cacheTrueKeys;
            private List<Key> _cacheFalseKeys;

            public GroupManager(Group group)
            {
                _group = group;
                _KeyHashset.UnionWith(_group.Keys);
            }

            public void SetState(Key key, bool state)
            {
                if (!_KeyHashset.Contains(key)) return;

                if (state)
                {
                    _trueKeyHashset.Add(key);
                }
                else
                {
                    _trueKeyHashset.Remove(key);
                }

                _cacheTrueKeys = null;
                _cacheFalseKeys = null;
            }

            public IEnumerable<Key> GetKeys(bool state)
            {
                if (state)
                {
                    if (_cacheTrueKeys == null)
                    {
                        _cacheTrueKeys = new List<Key>(_group.Keys.Where(n => _trueKeyHashset.Contains(n)));
                    }

                    return _cacheTrueKeys;
                }
                else
                {
                    if (_cacheFalseKeys == null)
                    {
                        _cacheFalseKeys = new List<Key>(_group.Keys.Where(n => !_trueKeyHashset.Contains(n)));
                    }

                    return _cacheFalseKeys;
                }
            }

            public int GetCount(bool state)
            {
                if (state)
                {
                    if (_cacheTrueKeys == null)
                    {
                        _cacheTrueKeys = new List<Key>(_group.Keys.Where(n => _trueKeyHashset.Contains(n)));
                    }

                    return _cacheTrueKeys.Count;
                }
                else
                {
                    if (_cacheFalseKeys == null)
                    {
                        _cacheFalseKeys = new List<Key>(_group.Keys.Where(n => !_trueKeyHashset.Contains(n)));
                    }

                    return _cacheFalseKeys.Count;
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
