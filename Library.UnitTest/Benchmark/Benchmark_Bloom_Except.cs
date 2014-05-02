using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Library.Collections;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        [Test]
        public void Bloom_Except()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            var set = new LockedSortedSet<int>();
            var list = new List<int>();

            for (int i = 0; i < 1024 * 1024; i++)
            {
                set.Add(random.Next(0, 1024 * 1024));
            }

            for (int i = 0; i < 1024 * 1024; i++)
            {
                list.Add(random.Next(0, 1024 * 1024 * 2));
            }

            for (int i = 0; i < 1024; i++)
            {
                List<int> result1 = null;
                List<int> result2 = null;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = set.ExceptFrom(list).ToList();
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = set.ExceptFrom_2(list).ToList();
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(CollectionUtilities.Equals(result1, result2));

                if (sw1.Elapsed.TotalSeconds >= 60) break;
                if (sw2.Elapsed.TotalSeconds >= 60) break;
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("ExceptFrom: " + sw1.Elapsed.ToString());
            sb.AppendLine("ExceptFrom_2: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}
