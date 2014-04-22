using System;
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
        public void Equals()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 256; i++)
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

                for (int j = 0; j < 32; j++)
                {
                    bool result = false;
                    bool result2 = false;

                    random.Shuffle(flags);
                    foreach (var index in flags)
                    {
                        if (index == 0)
                        {
                            sw1.Start();
                            result = Native.Equals(x, y);
                            sw1.Stop();
                        }
                        else if (index == 1)
                        {
                            sw2.Start();
                            result2 = Benchmark.Equals(x, y);
                            sw2.Stop();
                        }
                    }

                    Assert.IsTrue(result == result2);
                }
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Equals: " + sw1.Elapsed.ToString());
            sb.AppendLine("Unsafe Equals: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }

        public unsafe static bool Equals(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) return false;

            fixed (byte* p_x = x, p_y = y)
            {
                byte* t_x = p_x, t_y = p_y;
                int length = x.Length;

                for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8)
                {
                    if (*((long*)t_x) != *((long*)t_y)) return false;
                }

                if ((length & 4) != 0)
                {
                    if (*((int*)t_x) != *((int*)t_y)) return false;
                    t_x += 4; t_y += 4;
                }

                if ((length & 2) != 0)
                {
                    if (*((short*)t_x) != *((short*)t_y)) return false;
                    t_x += 2; t_y += 2;
                }

                if ((length & 1) != 0)
                {
                    if (*((byte*)t_x) != *((byte*)t_y)) return false;
                }

                return true;
            }
        }
    }
}
