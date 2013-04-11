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
            var targetList = new List<T>(collection);
            var responseList = new List<T>();

            while (targetList.Count > 0)
            {
                int i = _threadLocalRandom.Value.Next(targetList.Count);
                responseList.Add(targetList[i]);
                targetList.RemoveAt(i);
            }

            return responseList;
        }
    }
}