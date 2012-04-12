using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Library.Net
{
    /// <summary>
    /// ノード検索のためのメソッドを提供します
    /// </summary>
    public class Kademlia<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
        where T : INode
    {
        /// <summary>
        /// ノードリストの行数
        /// </summary>
        private int _row;

        /// <summary>
        /// ノードリストの列数
        /// </summary>
        private int _column;

        /// <summary>
        /// ノードリスト(k-buckets)
        /// </summary>
        private List<T>[] _nodesList;

        private T _baseNode;
        private object _thisLock = new object();

        /// <summary>
        /// RouteTableクラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="row">ノードリストの行数</param>
        /// <param name="column">ノードリストの列数</param>
        public Kademlia(int row, int column)
        {
            _row = row;
            _column = column;
            _nodesList = new List<T>[row];
        }

        /// <summary>
        /// 検索するハッシュを取得または設定します
        /// </summary>
        public T BaseNode
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _baseNode;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (value.Equals(default(T)))
                    {
                        _baseNode = default(T);
                    }
                    else
                    {
                        List<T> tempList = new List<T>();

                        for (int i = 0; i < _nodesList.Length; i++)
                        {
                            if (_nodesList[i] != null)
                            {
                                tempList.AddRange(_nodesList[i].ToArray().Reverse());
                            }
                        }

                        for (int i = 0; i < _nodesList.Length; i++)
                        {
                            _nodesList[i] = null;
                        }

                        _baseNode = value;

                        foreach (var item in tempList)
                        {
                            this.Add(item);
                        }
                    }
                }
            }
        }

        public int Count
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _nodesList.Where(n => n != null)
                        .Sum(m => m.Count);
                }
            }
        }

        public bool IsReadOnly
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return true;
                }
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                return this.ThisLock;
            }
        }

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion

        public static IEnumerable<T> Sort(T baseNode, byte[] id, IEnumerable<T> nodeList)
        {
            if (baseNode == null) throw new ArgumentNullException("baseNode");
            else if (baseNode.Id == null) throw new ArgumentNullException("baseNode.Id");
            else if (id == null) throw new ArgumentNullException("key");
            else if (nodeList == null) throw new ArgumentNullException("nodeList");

            var dic = new Dictionary<byte[], List<T>>(new BytesEqualityComparer());
            byte[] myXor = Kademlia<T>.Xor(id, baseNode.Id);

            foreach (var node in nodeList)
            {
                byte[] xor = Kademlia<T>.Xor(id, node.Id);

                if (Collection.Compare(myXor, xor) > 0)
                {
                    if (!dic.ContainsKey(xor))
                    {
                        dic[xor] = new List<T>();
                    }

                    dic[xor].Add(node);
                }
            }

            var list = new List<KeyValuePair<byte[], List<T>>>(dic);

            list.Sort(delegate(KeyValuePair<byte[], List<T>> x, KeyValuePair<byte[], List<T>> y)
            {
                return Collection.Compare(x.Key, y.Key);
            });

            var sumList = new List<T>();

            for (int i = 0; i < list.Count; i++)
            {
                sumList.AddRange(list[i].Value);
            }

            return sumList;
        }

        public IEnumerable<T> Search(byte[] id)
        {
            if (id == null) throw new ArgumentNullException("key");

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return Kademlia<T>.Sort(_baseNode, id, this.ToArray());
            }
        }

        public T Verify()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                List<INode> tempNodeList = new List<INode>();

                for (int i = _nodesList.Length - 1; i >= 0; i--)
                {
                    if (_nodesList[i].Count(n => n != null) < _column)
                    {
                        if (_nodesList[i - 1].Count == _column)
                        {
                            return _nodesList[i - 1][0];
                        }
                    }
                }

                return default(T);
            }
        }

        // Addより優先的に
        public void Live(T item)
        {
            if (item == null) throw new ArgumentNullException("item");
            else if (item.Id == null) throw new ArgumentNullException("item.Id");

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id);
                if (i == 0)
                {
                    return;
                }

                if (_nodesList[i - 1] != null)
                {
                    // 追加するnodeがNodeListに入っている場合
                    if (_nodesList[i - 1].Contains(item))
                    {
                        // そのノードをNodeListの末尾に移す
                        _nodesList[i - 1].Remove(item);
                        _nodesList[i - 1].Add(item);
                    }
                    else
                    {
                        // 列に空きがない場合、先頭のノード削除
                        if (_nodesList[i - 1].Count == _column)
                        {
                            _nodesList[i - 1].RemoveAt(0);
                        }

                        // そのノードを末尾に追加する
                        _nodesList[i - 1].Add(item);
                    }
                }
                else
                {
                    _nodesList[i - 1] = new List<T>();
                    _nodesList[i - 1].Add(item);
                }
            }
        }

        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException("item");
            else if (item.Id == null) throw new ArgumentNullException("item.Id");

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id);
                if (i == 0)
                {
                    return;
                }

                if (_nodesList[i - 1] != null)
                {
                    // 追加するnodeがNodeListに入っていない場合
                    if (!_nodesList[i - 1].Contains(item))
                    {
                        // 列に空きがある場合
                        if (_nodesList[i - 1].Count < _column)
                        {
                            // そのノードを先頭に追加する
                            _nodesList[i - 1].Insert(0, item);
                        }
                    }
                }
                else
                {
                    _nodesList[i - 1] = new List<T>();
                    _nodesList[i - 1].Add(item);
                }
            }
        }

        public void Clear()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                for (int i = 0; i < _nodesList.Length; i++)
                {
                    _nodesList[i] = null;
                }
            }
        }

        public bool Contains(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                List<T> tempList = new List<T>();

                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        tempList.AddRange(_nodesList[i].ToArray().Reverse());
                    }
                }

                return tempList.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                List<T> tempList = new List<T>();

                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        tempList.AddRange(_nodesList[i].ToArray().Reverse());
                    }
                }

                tempList.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            if (item == null) throw new ArgumentNullException("item");
            else if (item.Id == null) throw new ArgumentNullException("item.Id");

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id);
                if (i == 0)
                {
                    return false;
                }

                if (_nodesList[i - 1] != null)
                {
                    return _nodesList[i - 1].Remove(item);
                }

                return false;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        foreach (var node in _nodesList[i].ToArray().Reverse())
                        {
                            yield return node;
                        }
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.GetEnumerator();
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        private static int Distance(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                int length = Math.Max(x.Length, y.Length);
                int digit = 0;

                for (int i = 0; i < length; i++)
                {
                    byte value;

                    if (i >= x.Length) value = y[i];
                    else if (i >= y.Length) value = x[i];
                    else value = (byte)(x[i] ^ y[i]);

                    if ((value & 0x80) == 0x80) digit = 8;
                    else if ((value & 0x40) == 0x40) digit = 7;
                    else if ((value & 0x20) == 0x20) digit = 6;
                    else if ((value & 0x10) == 0x10) digit = 5;
                    else if ((value & 0x8) == 0x8) digit = 4;
                    else if ((value & 0x4) == 0x4) digit = 3;
                    else if ((value & 0x2) == 0x2) digit = 2;
                    else if ((value & 0x1) == 0x1) digit = 1;

                    if (digit != 0)
                    {
                        digit += (x.Length - (i + 1)) * 8;

                        break;
                    }
                }

                return digit;
            }
            else
            {
                int digit = 0;

                for (int i = 0; i < x.Length; i++)
                {
                    byte value = (byte)(x[i] ^ y[i]);

                    if ((value & 0x80) == 0x80) digit = 8;
                    else if ((value & 0x40) == 0x40) digit = 7;
                    else if ((value & 0x20) == 0x20) digit = 6;
                    else if ((value & 0x10) == 0x10) digit = 5;
                    else if ((value & 0x8) == 0x8) digit = 4;
                    else if ((value & 0x4) == 0x4) digit = 3;
                    else if ((value & 0x2) == 0x2) digit = 2;
                    else if ((value & 0x1) == 0x1) digit = 1;

                    if (digit != 0)
                    {
                        digit += (x.Length - (i + 1)) * 8;

                        break;
                    }
                }

                return digit;
            }
        }

        private static byte[] Xor(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                int length = Math.Min(x.Length, y.Length);
                byte[] buffer = new byte[Math.Max(x.Length, y.Length)];

                for (int i = 0; i < length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                if (x.Length > y.Length)
                {
                    for (int i = x.Length - y.Length; i < buffer.Length; i++)
                    {
                        buffer[i] = x[i];
                    }
                }
                else
                {
                    for (int i = y.Length - x.Length; i < buffer.Length; i++)
                    {
                        buffer[i] = y[i];
                    }
                }

                return buffer;
            }
            else
            {
                byte[] buffer = new byte[x.Length];

                for (int i = 0; i < x.Length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                return buffer;
            }
        }

        private class BytesEqualityComparer : IEqualityComparer<byte[]>
        {
            #region IEqualityComparer<byte[]> メンバ

            public bool Equals(byte[] x, byte[] y)
            {
                if ((x == null) != (y == null)) return false;

                if (x != null && y != null)
                {
                    if (!Collection.Equals(x, y))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(byte[] obj)
            {
                if (obj != null && obj.Length != 0)
                {
                    if (obj.Length >= 2) return BitConverter.ToUInt16(obj, 0);
                    else return obj[0];
                }
                else
                {
                    return 0;
                }
            }

            #endregion
        }
    }
}
