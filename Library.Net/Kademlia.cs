using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
        private static byte[] _distanceHashtable = new byte[256];

        private object _thisLock = new object();

        static Kademlia()
        {
            _distanceHashtable[0] = 0;
            _distanceHashtable[1] = 1;

            int i = 2;

            for (; i < 0x4; i++) _distanceHashtable[i] = 2;
            for (; i < 0x8; i++) _distanceHashtable[i] = 3;
            for (; i < 0x10; i++) _distanceHashtable[i] = 4;
            for (; i < 0x20; i++) _distanceHashtable[i] = 5;
            for (; i < 0x40; i++) _distanceHashtable[i] = 6;
            for (; i < 0x80; i++) _distanceHashtable[i] = 7;
            for (; i <= 0xff; i++) _distanceHashtable[i] = 8;
        }

        /// <summary>
        /// RouteTableクラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="row">ノードリストの行数</param>
        /// <param name="column">ノードリストの列数</param>
        public Kademlia(int row, int column)
        {
            if (row < 1) throw new ArgumentOutOfRangeException("row");
            if (column < 1) throw new ArgumentOutOfRangeException("column");

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
                lock (this.ThisLock)
                {
                    return _baseNode;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value.Equals(default(T)))
                    {
                        _baseNode = default(T);

                        this.Clear();
                    }
                    else
                    {
                        List<T> tempList = new List<T>();
                        tempList.AddRange(this.ToArray());

                        this.Clear();

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
                lock (this.ThisLock)
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
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                lock (this.ThisLock)
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

                    digit = _distanceHashtable[value];

                    if (digit != 0)
                    {
                        digit += (length - (i + 1)) * 8;

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

                    digit = _distanceHashtable[value];

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

        public static IEnumerable<T> Sort(byte[] baseId, byte[] targetId, IEnumerable<T> nodeList)
        {
            if (baseId == null) throw new ArgumentNullException("baseId");
            if (targetId == null) throw new ArgumentNullException("targetId");
            if (nodeList == null) throw new ArgumentNullException("nodeList");

            byte[] baseXor = Kademlia<T>.Xor(targetId, baseId);
            var list = new List<KeyValuePair<byte[], T>>();

            foreach (var node in nodeList)
            {
                byte[] xor = Kademlia<T>.Xor(targetId, node.Id);

                if (Collection.Compare(baseXor, xor) > 0)
                {
                    list.Add(new KeyValuePair<byte[], T>(xor, node));
                }
            }

            list.Sort((x, y) =>
            {
                return Collection.Compare(x.Key, y.Key);
            });

            var sumList = new List<T>();

            for (int i = 0; i < list.Count; i++)
            {
                sumList.Add(list[i].Value);
            }

            return sumList;
        }

        // Addより優先的に
        public void Live(T item)
        {
            if (_baseNode == null) throw new ArgumentNullException("BaseNode");
            if (_baseNode.Id == null) throw new ArgumentNullException("BaseNode.Id");
            if (item == null) throw new ArgumentNullException("item");
            if (item.Id == null) throw new ArgumentNullException("item.Id");

            lock (this.ThisLock)
            {
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id);
                if (i == 0) return;

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
            if (_baseNode == null) throw new ArgumentNullException("BaseNode");
            if (_baseNode.Id == null) throw new ArgumentNullException("BaseNode.Id");
            if (item == null) throw new ArgumentNullException("item");
            if (item.Id == null) throw new ArgumentNullException("item.Id");

            lock (this.ThisLock)
            {
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id);
                if (i == 0) return;

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

        public IEnumerable<T> Search(byte[] id)
        {
            if (_baseNode == null) throw new ArgumentNullException("BaseNode");
            if (_baseNode.Id == null) throw new ArgumentNullException("BaseNode.Id");
            if (id == null) throw new ArgumentNullException("key");

            lock (this.ThisLock)
            {
                return Kademlia<T>.Sort(_baseNode.Id, id, this.ToArray());
            }
        }

        public T Verify()
        {
            lock (this.ThisLock)
            {
                List<INode> tempNodeList = new List<INode>();

                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        if (_nodesList[i].Count == _column)
                        {
                            return _nodesList[i][0];
                        }
                    }
                }

                return default(T);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                for (int i = 0; i < _nodesList.Length; i++)
                {
                    _nodesList[i] = null;
                }
            }
        }

        public bool Contains(T item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (item.Id == null) throw new ArgumentNullException("item.Id");

            lock (this.ThisLock)
            {
                for (int i = _nodesList.Length - 1; i >= 0; i--)
                {
                    if (_nodesList[i] != null)
                    {
                        if (_nodesList[i].Contains(item)) return true;
                    }
                }

                return false;
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                List<T> tempList = new List<T>();

                for (int i = _nodesList.Length - 1; i >= 0; i--)
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
            if (item.Id == null) throw new ArgumentNullException("item.Id");

            lock (this.ThisLock)
            {
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id);
                if (i == 0) return false;

                if (_nodesList[i - 1] != null)
                {
                    return _nodesList[i - 1].Remove(item);
                }

                return false;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                for (int i = _nodesList.Length - 1; i >= 0; i--)
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
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                this.CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                var array = new T[this.Count];
                this.CopyTo(array, 0);

                return array;
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
