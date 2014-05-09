using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        [Test]
        public void Compare()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();
            Stopwatch sw3 = new Stopwatch();
            Stopwatch sw4 = new Stopwatch();

            var flags = new int[] { 0, 1, 2, 3 };

            for (int i = 0; i < 1024 * 256 * 32; i++)
            {
                byte[] x;
                byte[] y;

                if (random.Next(0, 2) == 0)
                {
                    var length = random.Next(0, 1024);
                    x = new byte[length];
                    y = new byte[length];
                }
                else
                {
                    x = new byte[random.Next(0, 1024)];
                    y = new byte[random.Next(0, 1024)];
                }

                if (random.Next(0, 2) == 0)
                {
                    random.NextBytes(x);
                    random.NextBytes(y);
                }

                int result1 = 0;
                int result2 = 0;
                int result3 = 0;
                int result4 = 0;

                var maxLength = Math.Min(x.Length, y.Length);

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = Unsafe.Compare(x, y);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = Unsafe.Compare2(x, y);
                        sw2.Stop();
                    }
                    else if (index == 2)
                    {
                        sw3.Start();
                        result3 = Unsafe.Compare(x, 0, y, 0, maxLength);
                        sw3.Stop();
                    }
                    else if (index == 3)
                    {
                        sw4.Start();
                        result4 = Unsafe.Compare2(x, 0, y, 0, maxLength);
                        sw4.Stop();
                    }
                }

                Assert.IsTrue(result1 == result2);
                Assert.IsTrue(result3 == result4);
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Compare: " + sw1.Elapsed.ToString());
            sb.AppendLine("Native Compare2: " + sw2.Elapsed.ToString());
            sb.AppendLine("Native Compare: " + sw3.Elapsed.ToString());
            sb.AppendLine("Native Compare2: " + sw4.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }

        //public unsafe static int Compare(byte[] source, byte[] destination)
        //{
        //    if (source == null) throw new ArgumentNullException("source");
        //    if (destination == null) throw new ArgumentNullException("destination");

        //    if (object.ReferenceEquals(source, destination)) return 0;
        //    if (source.Length != destination.Length) return (source.Length > destination.Length) ? 1 : -1;

        //    if (source.Length == 0) return 0;

        //    fixed (byte* p_x = source, p_y = destination)
        //    {
        //        byte* t_x = p_x, t_y = p_y;
        //        int length = source.Length;

        //        int c;

        //        for (int i = 0; i < length; i++)
        //        {
        //            if ((c = *t_x++ - *t_y++) != 0) return c;
        //        }
        //    }

        //    return 0;
        //}
    }
}
