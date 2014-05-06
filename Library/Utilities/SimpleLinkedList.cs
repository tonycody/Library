using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library
{
    internal class SimpleLinkedList<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable
    {
        private Node _firstNode;
        private int _count;

        private int? _capacity;

        private IEqualityComparer<T> _equalityComparer = EqualityComparer<T>.Default;

        private object _syncRoot = new object();

        public SimpleLinkedList()
        {

        }

        public SimpleLinkedList(int capacity)
        {
            _capacity = capacity;
        }

        public SimpleLinkedList(IEnumerable<T> collection)
        {
            foreach (var item in collection)
            {
                this.Add(item);
            }
        }

        protected virtual bool Filter(T item)
        {
            return false;
        }

        public void Add(T item)
        {
            if (_capacity != null && _count + 1 > _capacity.Value) throw new OverflowException();
            if (this.Filter(item)) return;

            var currentItem = new Node();
            currentItem.Value = item;
            currentItem.Next = _firstNode;

            _firstNode = currentItem;
            _count++;
        }

        public void Clear()
        {
            _firstNode = null;
            _count = 0;
        }

        public bool Contains(T item)
        {
            for (var currentNode = _firstNode; currentNode != null; currentNode = currentNode.Next)
            {
                if (_equalityComparer.Equals(currentNode.Value, item)) return true;
            }

            return false;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            for (var currentNode = _firstNode; currentNode != null; currentNode = currentNode.Next)
            {
                array[arrayIndex++] = currentNode.Value;
            }
        }

        public int Count
        {
            get
            {
                return _count;
            }
        }

        public bool Remove(T item)
        {
            Node currentItem = _firstNode;
            Node previousItem = null;

            while (currentItem != null)
            {
                if (_equalityComparer.Equals(currentItem.Value, item))
                {
                    if (previousItem == null)
                    {
                        _firstNode = _firstNode.Next;
                        _count--;

                        return true;
                    }
                    else
                    {
                        previousItem.Next = currentItem.Next;
                        _count--;

                        return true;
                    }
                }
                else
                {
                    previousItem = currentItem;
                    currentItem = currentItem.Next;
                }
            }

            return false;
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                return false;
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                return false;
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                return _syncRoot;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            for (var currentNode = _firstNode; currentNode != null; currentNode = currentNode.Next)
            {
                array.SetValue(currentNode.Value, index++);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var currentNode = _firstNode; currentNode != null; currentNode = currentNode.Next)
            {
                yield return currentNode.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private sealed class Node
        {
            public T Value { get; set; }
            public Node Next { get; set; }
        }
    }
}
