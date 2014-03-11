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

        private unsafe static byte[] Xor(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                if (x.Length < y.Length)
                {
                    fixed (byte* p_x = x, p_y = y)
                    {
                        byte* t_x = p_x, t_y = p_y;

                        byte[] buffer = new byte[y.Length];
                        int length = x.Length;

                        fixed (byte* p_buffer = buffer)
                        {
                            byte* t_buffer = p_buffer;

                            for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                            {
                                *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                            }

                            if ((length & 4) != 0)
                            {
                                *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                                t_x += 4; t_y += 4; t_buffer += 4;
                            }

                            if ((length & 2) != 0)
                            {
                                *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                                t_x += 2; t_y += 2; t_buffer += 2;
                            }

                            if ((length & 1) != 0)
                            {
                                *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                            }
                        }

                        Array.Copy(y, x.Length, buffer, x.Length, y.Length - x.Length);

                        return buffer;
                    }
                }
                else
                {
                    fixed (byte* p_x = x, p_y = y)
                    {
                        byte* t_x = p_x, t_y = p_y;

                        byte[] buffer = new byte[x.Length];
                        int length = y.Length;

                        fixed (byte* p_buffer = buffer)
                        {
                            byte* t_buffer = p_buffer;

                            for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                            {
                                *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                            }

                            if ((length & 4) != 0)
                            {
                                *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                                t_x += 4; t_y += 4; t_buffer += 4;
                            }

                            if ((length & 2) != 0)
                            {
                                *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                                t_x += 2; t_y += 2; t_buffer += 2;
                            }

                            if ((length & 1) != 0)
                            {
                                *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                            }
                        }

                        Array.Copy(x, y.Length, buffer, y.Length, x.Length - y.Length);

                        return buffer;
                    }
                }
            }
            else
            {
                fixed (byte* p_x = x, p_y = y)
                {
                    byte* t_x = p_x, t_y = p_y;

                    byte[] buffer = new byte[x.Length];
                    int length = x.Length;

                    fixed (byte* p_buffer = buffer)
                    {
                        byte* t_buffer = p_buffer;

                        for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                        {
                            *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                        }

                        if ((length & 4) != 0)
                        {
                            *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                            t_x += 4; t_y += 4; t_buffer += 4;
                        }

                        if ((length & 2) != 0)
                        {
                            *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                            t_x += 2; t_y += 2; t_buffer += 2;
                        }

                        if ((length & 1) != 0)
                        {
                            *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                        }
                    }

                    return buffer;
                }
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
