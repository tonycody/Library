using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Library.Net;
using Library.Net.Amoeba;
using NUnit.Framework;
using Library;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net")]
    public class Test_Library_Net
    {
        private Random _random = new Random();

        [Test]
        public void Test_Kademlia()
        {
            {
                var kademlia = new Kademlia<Node>(512, 20);

                {
                    Node node = null;

                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                        node = new Node(id, uris);
                    }

                    kademlia.BaseNode = node;
                }

                for (int i = 0; i < 1024; i++)
                {
                    Node node = null;

                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                        node = new Node(id, uris);
                    }

                    kademlia.Add(node);
                }

                for (int i = 0; i < 1024; i++)
                {
                    Node node = null;

                    {
                        var id = new byte[64];
                        _random.NextBytes(id);
                        var uris = new string[] { "net.tcp://localhost:9000", "net.tcp://localhost:9001", "net.tcp://localhost:9002" };

                        node = new Node(id, uris);
                    }

                    kademlia.Live(node);
                }

                foreach (var node in kademlia.ToArray().Take(kademlia.Count / 2))
                {
                    kademlia.Remove(node);
                }

                var v = kademlia.Verify();

                foreach (var node in kademlia.ToArray())
                {
                    var list = kademlia.Search(node.Id);
                    if (list.ElementAt(0) != node) throw new Exception();
                }

                {
                    var id = new byte[64];
                    _random.NextBytes(id);

                    var slist = Kademlia<Node>.Sort(kademlia.BaseNode.Id, id, kademlia.ToArray());
                }
            }

            {
                for (int i = 0; i < 1024; i++)
                {
                    var length = _random.Next(0, 1024);

                    byte[] x = new byte[length];
                    byte[] y = new byte[length];

                    _random.NextBytes(x);
                    _random.NextBytes(y);

                    var result = Test_Library_Net.Xor1(x, y);

                    Type t = typeof(Kademlia<Node>);
                    var result2 = (byte[])t.InvokeMember("Xor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod, null, null, new object[] { x, y });

                    Assert.IsTrue(Collection.Equals(result, result2));
                }

                {
                    Type t = typeof(Kademlia<Node>);
                    var result = (byte[])t.InvokeMember("Xor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod, null, null, new object[] { new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 0, 4, 4 } });

                    Assert.IsTrue(Collection.Equals(result, new byte[] { 0, 5, 6, 3, 4 }));
                }
            }

            // Benchmark
            {
                Stopwatch sw1 = new Stopwatch();
                Stopwatch sw2 = new Stopwatch();
                Stopwatch sw3 = new Stopwatch();
                Stopwatch sw4 = new Stopwatch();

                for (int i = 0; i < 1024 * 256 * 3; i++)
                {
                    byte[] x;
                    byte[] y;

                    if (_random.Next(0, 2) == 0)
                    {
                        var length = _random.Next(0, 1024);
                        x = new byte[length];
                        y = new byte[length];
                    }
                    else
                    {
                        x = new byte[_random.Next(0, 1024)];
                        y = new byte[_random.Next(0, 1024)];
                    }

                    _random.NextBytes(x);
                    _random.NextBytes(y);

                    byte[] result = null;
                    byte[] result2 = null;
                    byte[] result3 = null;
                    byte[] result4 = null;

                    foreach (var index in new int[] { 0, 1, 2, 3 }.Randomize())
                    {
                        if (index == 0)
                        {
                            sw1.Start();
                            result = Test_Library_Net.Xor1(x, y);
                            sw1.Stop();
                        }
                        else if (index == 1)
                        {
                            sw2.Start();
                            result2 = Test_Library_Net.Xor2(x, y);
                            sw2.Stop();
                        }
                        else if (index == 2)
                        {
                            sw3.Start();
                            result3 = Test_Library_Net.Xor3(x, y);
                            sw3.Stop();
                        }
                        else if (index == 3)
                        {
                            sw4.Start();
                            result4 = Test_Library_Net.Xor4(x, y);
                            sw4.Stop();
                        }
                    }

                    Assert.IsTrue(Unsafe.Equals(result, result2) && Unsafe.Equals(result, result3) && Unsafe.Equals(result, result4));
                }

                Debug.WriteLine("Xor1: " + sw1.Elapsed.ToString());
                Debug.WriteLine("Xor2: " + sw2.Elapsed.ToString());
                Debug.WriteLine("Xor3: " + sw3.Elapsed.ToString());
                Debug.WriteLine("Xor4: " + sw4.Elapsed.ToString());
            }
        }

        private static byte[] Xor1(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                byte[] buffer = new byte[Math.Max(x.Length, y.Length)];
                int length = Math.Min(x.Length, y.Length);

                for (int i = 0; i < length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                if (x.Length > y.Length)
                {
                    Array.Copy(x, y.Length, buffer, y.Length, x.Length - y.Length);
                }
                else
                {
                    Array.Copy(y, x.Length, buffer, x.Length, y.Length - x.Length);
                }

                return buffer;
            }
            else
            {
                byte[] buffer = new byte[x.Length];
                int length = x.Length;

                for (int i = 0; i < length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                return buffer;
            }
        }

        private unsafe static byte[] Xor2(byte[] x, byte[] y)
        {
            fixed (byte* p_x = x, p_y = y)
            {
                if (x.Length != y.Length)
                {
                    byte[] buffer = new byte[Math.Max(x.Length, y.Length)];
                    int length = Math.Min(x.Length, y.Length);

                    fixed (byte* p_buffer = buffer)
                    {
                        for (int i = 0; i < length; i++)
                        {
                            p_buffer[i] = (byte)(p_x[i] ^ p_y[i]);
                        }

                        if (x.Length > y.Length)
                        {
                            Array.Copy(x, y.Length, buffer, y.Length, x.Length - y.Length);
                        }
                        else
                        {
                            Array.Copy(y, x.Length, buffer, x.Length, y.Length - x.Length);
                        }
                    }

                    return buffer;
                }
                else
                {
                    byte[] buffer = new byte[x.Length];
                    int length = x.Length;

                    fixed (byte* p_buffer = buffer)
                    {
                        for (int i = 0; i < length; i++)
                        {
                            p_buffer[i] = (byte)(p_x[i] ^ p_y[i]);
                        }
                    }

                    return buffer;
                }
            }
        }

        private unsafe static byte[] Xor3(byte[] x, byte[] y)
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

        private unsafe static byte[] Xor4(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                fixed (byte* p_x = x, p_y = y)
                {
                    byte* t_x = p_x, t_y = p_y;

                    byte[] buffer = new byte[Math.Max(x.Length, y.Length)];
                    int length = Math.Min(x.Length, y.Length);

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

                    if (x.Length > y.Length)
                    {
                        Array.Copy(x, y.Length, buffer, y.Length, x.Length - y.Length);
                    }
                    else
                    {
                        Array.Copy(y, x.Length, buffer, x.Length, y.Length - x.Length);
                    }

                    return buffer;
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
    }
}
