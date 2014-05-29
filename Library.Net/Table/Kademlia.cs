using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

        private static readonly ThreadLocal<InfoManager> _threadLocalInfoManager = new ThreadLocal<InfoManager>(() => new InfoManager());

        public static IEnumerable<T> Search(byte[] targetId, byte[] baseId, IEnumerable<T> nodeList, int count)
        {
            if (targetId == null) throw new ArgumentNullException("targetId");
            if (baseId == null) throw new ArgumentNullException("baseId");
            if (nodeList == null) throw new ArgumentNullException("nodeList");

            if (count == 0) yield break;

            int InfoIndex = 0;

            var infoManager = _threadLocalInfoManager.Value;
            infoManager.SetBufferSize(targetId.Length);

            int linkIndex = 0;

            Info firstItem = null;
            Info lastItem = null;

            var baseItem = infoManager.GetInfo(InfoIndex++);
            Unsafe.Xor(targetId, baseId, baseItem.Xor);
            baseItem.Node = default(T);

            // 初期化。
            {
                firstItem = baseItem;
                lastItem = baseItem;

                linkIndex++;
            }

            // 挿入ソート（countが小さい場合、countで比較範囲を狭めた挿入ソートが高速。）
            foreach (var node in nodeList)
            {
                var targetItem = infoManager.GetInfo(InfoIndex++);
                Unsafe.Xor(targetId, node.Id, targetItem.Xor);
                targetItem.Node = node;

                var currentItem = lastItem;

                while (currentItem != null)
                {
                    if (Unsafe.Compare(currentItem.Xor, targetItem.Xor) <= 0) break;

                    currentItem = currentItem.Previous;
                }

                //　最前列に挿入。
                if (currentItem == null)
                {
                    firstItem.Previous = targetItem;
                    targetItem.Next = firstItem;
                    firstItem = targetItem;
                }
                // 最後尾に挿入。
                else if (lastItem == currentItem)
                {
                    currentItem.Next = targetItem;
                    targetItem.Previous = currentItem;
                    lastItem = targetItem;
                }
                // 中間に挿入。
                else
                {
                    var swapItem = currentItem.Next;

                    currentItem.Next = targetItem;
                    targetItem.Previous = currentItem;

                    targetItem.Next = swapItem;
                    swapItem.Previous = targetItem;
                }

                linkIndex++;

                // count数を超えている場合はlastItemを削除する。
                if (linkIndex > count)
                {
                    var previousItem = lastItem.Previous;

                    previousItem.Next = null;
                    lastItem = previousItem;

                    linkIndex--;
                }
            }

            for (var currentItem = firstItem; currentItem != null; currentItem = currentItem.Next)
            {
                // baseItem以上に距離が近いノードのみ許可する。
                if (currentItem == baseItem) yield break;

                yield return currentItem.Node;
            }
        }

        public static IEnumerable<T> Search(byte[] targetId, IEnumerable<T> nodeList, int count)
        {
            if (targetId == null) throw new ArgumentNullException("targetId");
            if (nodeList == null) throw new ArgumentNullException("nodeList");

            if (count == 0) yield break;

            int InfoIndex = 0;

            var infoManager = _threadLocalInfoManager.Value;
            infoManager.SetBufferSize(targetId.Length);

            int linkIndex = 0;

            Info firstItem = null;
            Info lastItem = null;

            // 挿入ソート（countが小さい場合、countで比較範囲を狭めた挿入ソートが高速。）
            foreach (var node in nodeList)
            {
                var targetItem = infoManager.GetInfo(InfoIndex++);
                Unsafe.Xor(targetId, node.Id, targetItem.Xor);
                targetItem.Node = node;

                var currentItem = lastItem;

                while (currentItem != null)
                {
                    if (Unsafe.Compare(currentItem.Xor, targetItem.Xor) <= 0) break;

                    currentItem = currentItem.Previous;
                }

                // 初期化。
                if (firstItem == null && lastItem == null)
                {
                    firstItem = targetItem;
                    lastItem = targetItem;
                }
                //　最前列に挿入。
                else if (currentItem == null)
                {
                    firstItem.Previous = targetItem;
                    targetItem.Next = firstItem;
                    firstItem = targetItem;
                }
                // 最後尾に挿入。
                else if (lastItem == currentItem)
                {
                    currentItem.Next = targetItem;
                    targetItem.Previous = currentItem;
                    lastItem = targetItem;
                }
                // 中間に挿入。
                else
                {
                    var swapItem = currentItem.Next;

                    currentItem.Next = targetItem;
                    targetItem.Previous = currentItem;

                    targetItem.Next = swapItem;
                    swapItem.Previous = targetItem;
                }

                linkIndex++;

                // count数を超えている場合はlastItemを削除する。
                if (linkIndex > count)
                {
                    var previousItem = lastItem.Previous;

                    previousItem.Next = null;
                    lastItem = previousItem;

                    linkIndex--;
                }
            }

            for (var currentItem = firstItem; currentItem != null; currentItem = currentItem.Next)
            {
                yield return currentItem.Node;
            }
        }

        // 自前のLinkedListのためのアイテムを用意。
        private sealed class Info
        {
            public T Node { get; set; }
            public byte[] Xor { get; set; }

            public Info Previous { get; set; }
            public Info Next { get; set; }
        }

        // Infoのプールを用意し、高速化を図る。
        private class InfoManager
        {
            private List<Info> _infos = new List<Info>();
            private int _bufferSize;

            public void SetBufferSize(int bufferSize)
            {
                if (bufferSize <= 0) throw new ArgumentOutOfRangeException("bufferSize");

                if (_bufferSize != bufferSize)
                {
                    _infos.Clear();
                }

                _bufferSize = bufferSize;
            }

            public Info GetInfo(int index)
            {
                if (index > 1024) return new Info() { Xor = new byte[_bufferSize] };

                for (int i = ((index + 1) - _infos.Count) - 1; i >= 0; i--)
                {
                    _infos.Add(new Info() { Xor = new byte[_bufferSize] });
                }

                var info = _infos[index];
                info.Previous = null;
                info.Next = null;

                return info;
            }
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
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id) - 1;
                if (i == -1) return;

                var targetList = _nodesList[i];

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
                    targetList.AddFirst(item);
                    _nodesList[i] = targetList;
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
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id) - 1;
                if (i == -1) return;

                var targetList = _nodesList[i];

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
                    _nodesList[i] = targetList;
                }
            }
        }

        public IEnumerable<T> Search(byte[] targetId, int count)
        {
            if (_baseNode == null) throw new ArgumentNullException("BaseNode");
            if (_baseNode.Id == null) throw new ArgumentNullException("BaseNode.Id");
            if (targetId == null) throw new ArgumentNullException("targetId");
            if (count < 0) throw new ArgumentOutOfRangeException("count");

            lock (this.ThisLock)
            {
                return Kademlia<T>.Search(targetId, _baseNode.Id, this.ToArray(), count);
            }
        }

        public T Verify()
        {
            lock (this.ThisLock)
            {
                List<INode> tempNodeList = new List<INode>();

                for (int i = _nodesList.Length - 1; i >= 0; i--)
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
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id) - 1;
                if (i == -1) return false;

                var targetList = _nodesList[i];

                if (targetList != null)
                {
                    return targetList.Contains(item);
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
                        tempList.AddRange(_nodesList[i]);
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
                int i = Kademlia<T>.Distance(this.BaseNode.Id, item.Id) - 1;
                if (i == -1) return false;

                var targetList = _nodesList[i];

                if (targetList != null)
                {
                    return targetList.Remove(item);
                }

                return false;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                List<T> tempList = new List<T>();

                for (int i = 0; i < _nodesList.Length; i++)
                {
                    if (_nodesList[i] != null)
                    {
                        tempList.AddRange(_nodesList[i]);
                    }
                }

                ((ICollection)tempList).CopyTo(array, index);
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
