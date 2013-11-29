using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Library.Collections;
using NUnit.Framework;
using System.Threading;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Collections")]
    public class Test_Library_Collections
    {
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
    }
}
