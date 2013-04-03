using System;
using System.Collections.Generic;
using System.Linq;

namespace Library
{
    public static class Collection
    {
        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals(byte[] sourceCollection, byte[] destinationCollection)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            if (sourceCollection.Length != destinationCollection.Length) return false;

            for (int i = 0; i < sourceCollection.Length; i++)
            {
                if (sourceCollection[i] != destinationCollection[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IList<T> sourceCollection, IList<T> destinationCollection)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            if (sourceCollection.Count != destinationCollection.Count) return false;

            for (int i = 0; i < sourceCollection.Count; i++)
            {
                if (sourceCollection[i] == null)
                {
                    if (destinationCollection[i] == null) continue;

                    if (!destinationCollection[i].Equals(sourceCollection[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!sourceCollection[i].Equals(destinationCollection[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] == null)
                {
                    if (list2[i] == null) continue;

                    if (!list2[i].Equals(list1[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!list1[i].Equals(list2[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals(byte[] sourceCollection, int sourceIndex, byte[] destinationCollection, int destinationIndex, int length)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (0 > (sourceCollection.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destinationCollection.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (sourceCollection.Length - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destinationCollection.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (sourceCollection[i] != destinationCollection[j])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IList<T> sourceCollection, int sourceIndex, IList<T> destinationCollection, int destinationIndex, int length)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (0 > (sourceCollection.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destinationCollection.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (sourceCollection.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destinationCollection.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (sourceCollection[i] == null)
                {
                    if (destinationCollection[j] == null) continue;

                    if (!destinationCollection[j].Equals(sourceCollection[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!sourceCollection[i].Equals(destinationCollection[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (0 > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (list1[i] == null)
                {
                    if (list2[j] == null) continue;

                    if (!list2[j].Equals(list1[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!list1[i].Equals(list2[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IList<T> sourceCollection, IList<T> destinationCollection, IEqualityComparer<T> equalityComparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            if (sourceCollection.Count != destinationCollection.Count) return false;

            for (int i = 0; i < sourceCollection.Count; i++)
            {
                if (!equalityComparer.Equals(sourceCollection[i], destinationCollection[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection, IEqualityComparer<T> equalityComparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                if (!equalityComparer.Equals(list1[i], list2[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IList<T> sourceCollection, int sourceIndex, IList<T> destinationCollection, int destinationIndex, int length, IEqualityComparer<T> equalityComparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (0 > (sourceCollection.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destinationCollection.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (sourceCollection.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destinationCollection.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (!equalityComparer.Equals(sourceCollection[i], destinationCollection[j]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length, IEqualityComparer<T> equalityComparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (0 > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (!equalityComparer.Equals(list1[i], list2[j]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare(byte[] sourceCollection, byte[] destinationCollection)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            int c = 0;
            if ((c = sourceCollection.Length - destinationCollection.Length) != 0) return c;

            for (int i = 0; i < sourceCollection.Length; i++)
            {
                if ((c = sourceCollection[i].CompareTo(destinationCollection[i])) != 0) return c;
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IList<T> sourceCollection, IList<T> destinationCollection)
            where T : IComparable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            int c = 0;
            if ((c = sourceCollection.Count - destinationCollection.Count) != 0) return c;

            for (int i = 0; i < sourceCollection.Count; i++)
            {
                if (sourceCollection[i] == null)
                {
                    if (destinationCollection[i] == null) continue;

                    if ((c = destinationCollection[i].CompareTo(sourceCollection[i])) != 0) return c * -1;
                }
                else
                {
                    if ((c = sourceCollection[i].CompareTo(destinationCollection[i])) != 0) return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection)
            where T : IComparable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            int c = 0;
            if ((c = list1.Count - list2.Count) != 0) return c;

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] == null)
                {
                    if (list2[i] == null) continue;

                    if ((c = list2[i].CompareTo(list1[i])) != 0) return c * -1;
                }
                else
                {
                    if ((c = list1[i].CompareTo(list2[i])) != 0) return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare(byte[] sourceCollection, int sourceIndex, byte[] destinationCollection, int destinationIndex, int length)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (0 > (sourceCollection.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destinationCollection.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");

            int c = 0;
            if ((c = Math.Min(length, (sourceCollection.Length - sourceIndex)) - Math.Min(length, (destinationCollection.Length - destinationIndex))) != 0) return c;

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if ((c = sourceCollection[i].CompareTo(destinationCollection[j])) != 0) return c;
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IList<T> sourceCollection, int sourceIndex, IList<T> destinationCollection, int destinationIndex, int length)
            where T : IComparable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (0 > (sourceCollection.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destinationCollection.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");

            int c = 0;
            if ((c = Math.Min(length, (sourceCollection.Count - sourceIndex)) - Math.Min(length, (destinationCollection.Count - destinationIndex))) != 0) return c;

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (sourceCollection[i] == null)
                {
                    if (destinationCollection[j] == null) continue;

                    if ((c = destinationCollection[j].CompareTo(sourceCollection[i])) != 0) return c * -1;
                }
                else
                {
                    if ((c = sourceCollection[i].CompareTo(destinationCollection[j])) != 0) return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length)
            where T : IComparable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (0 > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");

            int c = 0;
            if ((c = Math.Min(length, (list1.Count - sourceIndex)) - Math.Min(length, (list2.Count - destinationIndex))) != 0) return c;

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (list1[i] == null)
                {
                    if (list2[j] == null) continue;

                    if ((c = list2[j].CompareTo(list1[i])) != 0) return c * -1;
                }
                else
                {
                    if ((c = list1[i].CompareTo(list2[j])) != 0) return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IList<T> sourceCollection, IList<T> destinationCollection, IComparer<T> comparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            if (sourceCollection.Count != destinationCollection.Count)
            {
                return sourceCollection.Count - destinationCollection.Count;
            }

            for (int i = 0; i < sourceCollection.Count; i++)
            {
                int c = 0;
                if ((c = comparer.Compare(sourceCollection[i], destinationCollection[i])) != 0) return c;
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection, IComparer<T> comparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count)
            {
                return list1.Count - list2.Count;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                int c = 0;
                if ((c = comparer.Compare(list1[i], list2[i])) != 0) return c;
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IList<T> sourceCollection, int sourceIndex, IList<T> destinationCollection, int destinationIndex, int length, IComparer<T> comparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (0 > (sourceCollection.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destinationCollection.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");

            int c = 0;
            if ((c = Math.Min(length, (sourceCollection.Count - sourceIndex)) - Math.Min(length, (destinationCollection.Count - destinationIndex))) != 0) return c;

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if ((c = comparer.Compare(sourceCollection[i], destinationCollection[j])) != 0) return c;
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length, IComparer<T> comparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (comparer == null) throw new ArgumentNullException("comparer");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (0 > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");

            int c = 0;
            if ((c = Math.Min(length, (list1.Count - sourceIndex)) - Math.Min(length, (list2.Count - destinationIndex))) != 0) return c;

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if ((c = comparer.Compare(list1[i], list2[j])) != 0) return c;
            }

            return 0;
        }
    }
}
