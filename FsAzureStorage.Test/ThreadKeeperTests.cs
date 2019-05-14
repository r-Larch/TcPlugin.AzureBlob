using System;
using System.Threading;
using System.Threading.Tasks;
using LarchSys.FsAzureStorage;
using Microsoft.VisualStudio.TestTools.UnitTesting;


// ReSharper disable InconsistentNaming
// ReSharper disable LocalizableElement
namespace FsAzureStorage.Test {
    [TestClass]
    public class ThreadKeeperTests {
        [TestMethod]
        public void Test_MultiThreading()
        {
            // TODO overwrite: public virtual int OnTcPluginEvent(PluginEventArgs e)

            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Console.WriteLine($"Main ThreadId {mainThreadId}");

            var t = new ThreadKeeper();


            var result = t.ExecAsync(async (token) => {
                await Task.Delay(1, token);
                Console.WriteLine($"   async ThreadId {Thread.CurrentThread.ManagedThreadId}");
                Assert.AreNotEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);

                Parallel.For(0, 100, new ParallelOptions {
                    MaxDegreeOfParallelism = 100
                }, i => {
                    var tid = Thread.CurrentThread.ManagedThreadId;
                    Console.WriteLine($"{i}      ThreadId {tid}");
                    Assert.AreNotEqual(mainThreadId, tid);

                    var ui_result = t.RunInMainThread(() => {
                        Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId);
                        if (i == 20) {
                            t.RunInMainThread(() => Console.WriteLine("hoi"));
                        }

                        return true;
                    });

                    if (i == 20) {
                        t.RunInMainThread(() => Console.WriteLine("hoi outer"));
                    }

                    if (i == 30) {
                        try {
                            t.RunInMainThread(() => throw new Exception("Test"));
                        }
                        catch (Exception e) {
                            Console.WriteLine($"caught: {e.Message}");
                        }
                    }

                    Assert.AreEqual(true, ui_result);
                });

                return 12;
            });

            Console.WriteLine($"result: {result}");
        }
    }
}
