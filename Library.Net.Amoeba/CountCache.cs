using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    // パフォーマンス上の理由から仕方なく、これは高速化にかなり貢献してる

    sealed class CountCache
    {
        private Dictionary<Group, HashSet<Key>> _keyTrueList = new Dictionary<Group, HashSet<Key>>();
        private Dictionary<Group, HashSet<Key>> _keyFalseList = new Dictionary<Group, HashSet<Key>>();

        private Dictionary<Group, List<Key>> _getTrueKeys = new Dictionary<Group, List<Key>>();
        private Dictionary<Group, List<Key>> _getFalseKeys = new Dictionary<Group, List<Key>>();
        private Dictionary<Group, int> _getCount = new Dictionary<Group, int>();

        private object _thisLock = new object();

        public void SetGroup(Group group)
        {
            lock (this.ThisLock)
            {
                if (!_keyTrueList.ContainsKey(group))
                {
                    _keyTrueList.Add(group, new HashSet<Key>());
                }

                if (!_keyFalseList.ContainsKey(group))
                {
                    _keyFalseList.Add(group, new HashSet<Key>(group.Keys));
                }
            }
        }

        public void SetKey(Key key, bool flag)
        {
            lock (this.ThisLock)
            {
                if (flag)
                {
                    var groups = _keyFalseList.Where(n => n.Value.Contains(key)).Select(n => n.Key);

                    foreach (var group in groups)
                    {
                        _keyTrueList[group].Add(key);
                        _keyFalseList[group].Remove(key);

                        _getCount.Remove(group);
                        _getTrueKeys.Remove(group);
                        _getFalseKeys.Remove(group);
                    }
                }
                else
                {
                    var groups = _keyTrueList.Where(n => n.Value.Contains(key)).Select(n => n.Key);

                    foreach (var group in groups)
                    {
                        _keyFalseList[group].Add(key);
                        _keyTrueList[group].Remove(key);

                        _getCount.Remove(group);
                        _getTrueKeys.Remove(group);
                        _getFalseKeys.Remove(group);
                    }
                }
            }
        }

        public IEnumerable<Key> GetKeys(Group group, bool flag)
        {
            lock (this.ThisLock)
            {
                if (flag)
                {
                    if (_getTrueKeys.ContainsKey(group))
                    {
                        return _getTrueKeys[group];
                    }
                    else if (_keyTrueList.ContainsKey(group))
                    {
                        _getTrueKeys[group] = new List<Key>(group.Keys.Where(n => _keyTrueList[group].Contains(n)));

                        return _getTrueKeys[group];
                    }
                }
                else
                {
                    if (_getFalseKeys.ContainsKey(group))
                    {
                        return _getFalseKeys[group];
                    }
                    else if (_keyTrueList.ContainsKey(group))
                    {
                        _getFalseKeys[group] = new List<Key>(group.Keys.Where(n => _keyFalseList[group].Contains(n)));

                        return _getFalseKeys[group];
                    }
                }

                return new KeyCollection();
            }
        }

        public int GetCount(Group group)
        {
            lock (this.ThisLock)
            {
                if (_getCount.ContainsKey(group))
                {
                    return _getCount[group];
                }
                else if (_keyTrueList.ContainsKey(group))
                {
                    _getCount[group] = group.Keys.Where(n => _keyTrueList[group].Contains(n)).Count();

                    return _getCount[group];
                }

                return 0;
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
