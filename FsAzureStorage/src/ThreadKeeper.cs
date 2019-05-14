using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;


namespace FsAzureStorage {
    public class ThreadKeeper : IDisposable {
        public int MainThreadId { get; }

        private readonly AutoResetEvent _resetEvent1;
        private readonly ConcurrentQueue<MyFunc> _queue;
        private readonly CancellationTokenSource _token;
        public CancellationToken Token => _token.Token;

        private class MyFunc {
            public Func<object> Func { get; set; }
            public object Result { get; set; }
            public Exception Exception { get; set; }
            public AutoResetEvent Reset { get; set; }
        }

        public void Cancel()
        {
            _token.Cancel();
        }

        public ThreadKeeper()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _resetEvent1 = new AutoResetEvent(false);
            _queue = new ConcurrentQueue<MyFunc>();
            _token = new CancellationTokenSource();
        }


        public void RunInMainThread(Action action)
        {
            var _ = RunInMainThread(() => {
                action();
                return (object) null;
            });
        }


        public T RunInMainThread<T>(Func<T> func)
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThreadId) {
                return func();
            }

            var item = new MyFunc {
                Func = () => (object) func(),
                Reset = new AutoResetEvent(false),
            };

            _queue.Enqueue(item);

            _resetEvent1.Set();
            WaitOne(item.Reset, Token);
            item.Reset.Dispose();

            if (item.Exception != null) {
                throw item.Exception;
            }

            return (T) item.Result;
        }


        public T ExecAsync<T>(Func<CancellationToken, Task<T>> asyncFunc)
        {
            var task = asyncFunc(Token);
            task.ContinueWith(t => {
                _resetEvent1.Set();
            }, Token);

            while (!task.IsCompleted) {
                WaitOne(_resetEvent1, Token);

                while (_queue.TryDequeue(out var item)) {
                    try {
                        item.Result = item.Func();
                    }
                    catch (Exception e) {
                        item.Exception = e;
                    }

                    item.Reset.Set();
                }
            }

            var ret = task.Result;
            return ret;
        }


        private static bool WaitOne(WaitHandle handle, CancellationToken token)
        {
            var n = WaitHandle.WaitAny(new[] {handle, token.WaitHandle}, Timeout.Infinite);
            switch (n) {
                case WaitHandle.WaitTimeout:
                    return false;
                case 0:
                    return true;
                default:
                    token.ThrowIfCancellationRequested();
                    return false; // never reached
            }
        }


        public void Dispose()
        {
            _token.Dispose();
            _resetEvent1.Dispose();
        }
    }
}
