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
        private int _row;
        private int _column;
        private T _baseNode;

        private LinkedList<T>[] _nodesList;

        private static byte[] _distanceHashtable = new byte[256];

        private readonly object _thisLock = new object();

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
        /// Kademliaクラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="row">ノードリストの行数</param>
        /// <param name="column">ノードリストの列数</param>
        public Kademlia(int row, int column)
        {
            if (row <= 0) throw new ArgumentOutOfRangeException("row");
            if (column <= 0) throw new ArgumentOutOfRangeException("column");

            _row = row;
            _column = column;
            _nodesList = new LinkedList<T>[row];
        }

        /// <summary>
        /// ベースとなるNodeを取得または設定します
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
                    var tempList = this.ToList();
                    this.Clear();

                    _baseNode = value;

                    foreach (var item in tempList)
                    {
                        this.Add(item);
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
                    return _nodesList
                        .Where(n => n != null)
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

        public static IEnumerable<T> Sort(byte[] baseId, byte[] targetId, IEnumerable<T> nodeList, int count)
        {
            if (baseId == null) throw new ArgumentNullException("baseId");
            if (targetId == null) throw new ArgumentNullException("targetId");
            if (nodeList == null) throw new ArgumentNullException("nodeList");

            byte[] baseXor = Native.Xor(targetId, baseId);

            var targetList = new LinkedList<Pair>();

            foreach (var node in nodeList)
            {
                byte[] targetXor = Native.Xor(targetId, node.Id);
                if (Native.Compare(baseXor, targetXor) <= 0) continue;

                // 挿入ソート（countが小さい場合、countで比較範囲を狭めた挿入ソートが高速。）
                var current = targetList.Last;

                while (current != null)
                {
                    var pair = current.Value;
                    if (Native.Compare(pair.Xor, targetXor) <= 0) break;

                    current = current.Previous;
                }

                if (current == null) targetList.AddFirst(new Pair() { Xor = targetXor, Node = node });
                else targetList.AddAfter(current, new Pair() { Xor = targetXor, Node = node });

                if (targetList.Count > count) targetList.RemoveLast();
            }

            return targetList.Select(n => n.Node);
        }

        private class Pair
        {
            public byte[] Xor { get; set; }
            public T Node { get; set; }
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

                var targetList = _nodesList[i - 1];

                // 生存率の高いNodeはFirstに、そうでないNodeはLastに
                if (targetList != null)
                {
                    if (targetList.Contains(item))
                    {
                        targetList.Remove(item);
                        targetList.AddFirst(item);
                    }
                    else
                    {
                        if (targetList.Count == _column)
                        {
                            targetList.RemoveLast();
                        }

                        targetList.AddFirst(item);
                    }
                }
                else
                {
                    targetList = new LinkedList<T>();
                    targetList.AddLast(item);
                    _nodesList[i - 1] = targetList;
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

                var targetList = _nodesList[i - 1];

                if (targetList != null)
                {
                    // 生存率の高いNodeはFirstに、そうでないNodeはLastに
                    if (!targetList.Contains(item))
                    {
                        if (targetList.Count < _column)
                        {
                            targetList.AddLast(item);
                        }
                    }
                }
                else
                {
                    targetList = new LinkedList<T>();
                    targetList.AddLast(item);
                    _nodesList[i - 1] = targetList;
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
                return Kademlia<T>.Sort(_baseNode.Id, id, this.ToArray(), this.Count);
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
                            return _nodesList[i].Last.Value;
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

                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        tempList.AddRange(_nodesList[i].ToArray());
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

                var targetList = _nodesList[i - 1];

                if (targetList != null)
                {
                    return targetList.Remove(item);
                }

                return false;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        foreach (var node in _nodesList[i].ToArray())
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
