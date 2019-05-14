using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;


namespace FsAzureStorage {
    public class ThreadKeeper2 : IDisposable {
        public int MainThreadId { get; }

        private readonly ConcurrentQueue<Action> _queue;
        private readonly CancellationTokenSource _token;

        public ThreadKeeper2()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            _queue = new ConcurrentQueue<Action>();
            _token = new CancellationTokenSource();
        }

        public void Cancel()
        {
            _token.Cancel();
        }

        public void RunInMainThread(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThreadId) {
                action();
                return;
            }

            _queue.Enqueue(action);
        }


        public T ExecAsync<T>(Func<CancellationToken, Task<T>> asyncFunc)
        {
            var task = asyncFunc(_token.Token);

            while (!task.IsCompleted) {
                while (_queue.TryDequeue(out var action)) {
                    action();
                }

                Thread.Sleep(1);
            }

            var ret = task.Result;

            return ret;
        }

        public void Dispose()
        {
            _token.Dispose();
        }
    }
}
