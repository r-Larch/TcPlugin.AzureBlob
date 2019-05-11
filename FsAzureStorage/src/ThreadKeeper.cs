using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;


namespace LarchSys.FsAzureStorage {
    public class ThreadKeeper : IDisposable {
        public int MainThreadId { get; }

        private readonly AutoResetEvent _resetEvent1;
        private readonly AutoResetEvent _resetEvent2;
        private readonly ConcurrentQueue<MyFunc> _queue;
        private readonly object _look;

        private class MyFunc {
            public Func<object> Func { get; set; }
            public object Result { get; set; }
            public Exception Exception { get; set; }
        }

        public ThreadKeeper()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _look = new object();
            _resetEvent1 = new AutoResetEvent(false);
            _resetEvent2 = new AutoResetEvent(false);
            _queue = new ConcurrentQueue<MyFunc>();
        }


        public T RunInMainThread<T>(Func<T> func)
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThreadId) {
                return func();
            }

            //lock (_look) {

            //}

            var lockTaken = false;
            try {
                Monitor.TryEnter(_look, millisecondsTimeout: 1, ref lockTaken);
                if (lockTaken) {
                    // The critical section.

                    var item = new MyFunc {Func = () => (object) func()};
                    _queue.Enqueue(item);
                    Trace.WriteLine("_queue.Enqueue done");

                    Trace.WriteLine("_resetEvent1.Set();");
                    _resetEvent1.Set();
                    Trace.WriteLine("_resetEvent2.WaitOne();");
                    _resetEvent2.WaitOne();

                    if (item.Exception != null) {
                        throw item.Exception;
                    }

                    return (T) item.Result;
                }
                else {
                    // The lock was not acquired.
                    Trace.WriteLine($"skip [T{Thread.CurrentThread.ManagedThreadId}]");

                    return default;
                }
            }
            finally {
                // Ensure that the lock is released.
                if (lockTaken) {
                    Monitor.Exit(_look);
                }
            }
        }


        public T ExecAsync<T>(Func<Task<T>> asyncFunc)
        {
            var task = asyncFunc();
            task.ContinueWith(t => {
                Trace.WriteLine("task done");
                Trace.WriteLine("task _resetEvent1.Set();");
                Trace.WriteLine("task _resetEvent2.Set();");
                _resetEvent1.Set();
                _resetEvent2.Set();
            });

            while (!task.IsCompleted) {
                Trace.WriteLine("main _resetEvent1.WaitOne();");
                _resetEvent1.WaitOne();

                while (_queue.TryDequeue(out var item)) {
                    Trace.WriteLine("main _queue.TryDequeue(..);");
                    try {
                        item.Result = item.Func();
                    }
                    catch (Exception e) {
                        Trace.WriteLine($"main error in func: {e.Message}");
                        item.Exception = e;
                    }
                }

                _resetEvent2.Set();
                Trace.WriteLine("main _resetEvent2.Set();");
            }

            var ret = task.Result;

            return ret;
        }

        public void Dispose()
        {
            _resetEvent1?.Dispose();
            _resetEvent2?.Dispose();
        }
    }
}
