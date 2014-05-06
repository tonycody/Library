using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Collections")]
    public class Test_Library_Collections
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_WaitQueue()
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("oo");

            // アイテムが無い場合、Dequeueは待機される。
            {
                WaitQueue<string> queue = new WaitQueue<string>();

                var task1 = Task.Factory.StartNew(() =>
                {
                    Assert.AreEqual(queue.WaitDequeue(TimeSpan.Zero), false);

                    Assert.Throws<TimeoutException>(() =>
                    {
                        queue.Dequeue(TimeSpan.Zero);
                    });

                    Assert.AreEqual(queue.Dequeue(), "Test");
                });

                var task2 = Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(1000 * 3);

                    queue.Enqueue("Test");
                });

                Task.WaitAll(task1, task2);
            }

            // キャパシティを超えるとEnqueueが待機状態になるため、4つ目のアイテムがタイムアウトの例外を投げる。
            {
                WaitQueue<string> queue = new WaitQueue<string>(3);

                queue.Enqueue("1", TimeSpan.Zero);
                queue.Enqueue("2", TimeSpan.Zero);
                queue.Enqueue("3", TimeSpan.Zero);

                Assert.Throws<TimeoutException>(() =>
                {
                    queue.Enqueue("4", TimeSpan.Zero);
                });
            }

            // キャパシティを超えるとEnqueueが待機され、Dequeueによって項目が減るとEnqueueが再開される。
            {
                WaitQueue<string> queue = new WaitQueue<string>(3);

                queue.Enqueue("1", TimeSpan.Zero);
                queue.Enqueue("2", TimeSpan.Zero);
                queue.Enqueue("3", TimeSpan.Zero);

                var task1 = Task.Factory.StartNew(() =>
                {
                    Assert.AreEqual(queue.WaitEnqueue(TimeSpan.Zero), false);

                    Thread.Sleep(1000 * 3);

                    Assert.DoesNotThrow(() =>
                    {
                        queue.Enqueue("4", TimeSpan.Zero);
                    });
                });

                var task2 = Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(1000 * 1);

                    queue.Dequeue();
                });

                Task.WaitAll(task1, task2);
            }
        }

        [Test]
        public void Test_BinaryArray()
        {
            using (BinaryArray binaryArray = new BinaryArray(1024 * 256))
            {
                Random random_a, random_b;

                {
                    var seed = _random.Next();

                    random_a = new Random(seed);
                    random_b = new Random(seed);
                }

                {
                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_a.Next(0, 1024 * 256);
                        binaryArray.Set(p, true);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_b.Next(0, 1024 * 256);
                        Assert.IsTrue(binaryArray.Get(p));
                    }

                    {
                        int count = 0;

                        for (int i = 0; i < 1024 * 256; i++)
                        {
                            if (binaryArray.Get(i)) count++;
                        }

                        Assert.IsTrue(count <= 1024);
                    }
                }

                {
                    for (int i = 0; i < 1024 * 256; i++)
                    {
                        binaryArray.Set(i, true);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_a.Next(0, 1024 * 256);
                        binaryArray.Set(p, false);
                    }

                    for (int i = 0; i < 1024; i++)
                    {
                        var p = random_b.Next(0, 1024 * 256);
                        Assert.IsTrue(!binaryArray.Get(p));
                    }

                    {
                        int count = 0;

                        for (int i = 0; i < 1024 * 256; i++)
                        {
                            if (!binaryArray.Get(i)) count++;
                        }

                        Assert.IsTrue(count <= 1024);
                    }
                }
            }
        }
    }
}
