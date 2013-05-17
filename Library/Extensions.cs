using System;
using System.Collections.Generic;
using System.Threading;

namespace Library
{
    public static class IEnumerableExtensions
    {
        private static readonly ThreadLocal<Random> _threadLocalRandom = new ThreadLocal<Random>(() => new Random());

        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> collection)
        {
            var random = _threadLocalRandom.Value;
            var list = new List<T>(collection);
            int n = list.Count;

            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = list[n];
                list[n] = list[k];
                list[k] = temp;
            }

            return list;
        }

        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> collection, Random random)
        {
            var list = new List<T>(collection);
            int n = list.Count;

            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = list[n];
                list[n] = list[k];
                list[k] = temp;
            }

            return list;
        }
    }

    public static class RandomExtensions
    {
        public static void Shuffle<T>(this Random random, T[] array)
        {
            int n = array.Length;

            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = array[n];
                array[n] = array[k];
                array[k] = temp;
            }
        }
    }
}
