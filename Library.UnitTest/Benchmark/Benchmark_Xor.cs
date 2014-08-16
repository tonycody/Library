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
        public void Xor()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 4; i++)
            {
                byte[] x;
                byte[] y;

                if (random.Next(0, 2) == 0)
                {
                    var length = random.Next(0, 1024 * 256);
                    x = new byte[length];
                    y = new byte[length];
                }
                else
                {
                    x = new byte[random.Next(0, 1024 * 256)];
                    y = new byte[random.Next(0, 1024 * 256)];
                }

                random.NextBytes(x);
                random.NextBytes(y);

                byte[] result1 = new byte[Math.Min(x.Length, y.Length)];
                byte[] result2 = new byte[Math.Min(x.Length, y.Length)];

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        Unsafe.Xor(x, y, result1);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        Benchmark.Xor(x, y, result2);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(Unsafe.Equals(result1, result2));
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Xor: " + sw1.Elapsed.ToString());
            sb.AppendLine("Unsafe Xor: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }

        public unsafe static void Xor(byte[] source1, byte[] source2, byte[] destination)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");
            if (destination == null) throw new ArgumentNullException("destination");

            // Zero
            {
                int targetRange = Math.Max(source1.Length, source2.Length);

                if (destination.Length > targetRange)
                {
                    Unsafe.Zero(destination, targetRange, destination.Length - targetRange);
                }
            }

            if (source1.Length > source2.Length && destination.Length > source2.Length)
            {
                Unsafe.Copy(source1, source2.Length, destination, source2.Length, Math.Min(source1.Length, destination.Length) - source2.Length);
            }
            else if (source2.Length > source1.Length && destination.Length > source1.Length)
            {
                Unsafe.Copy(source2, source1.Length, destination, source1.Length, Math.Min(source2.Length, destination.Length) - source1.Length);
            }

            int length = Math.Min(Math.Min(source1.Length, source2.Length), destination.Length);

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x, t_y = p_y;

                fixed (byte* p_buffer = destination)
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
            }
        }
    }
}
