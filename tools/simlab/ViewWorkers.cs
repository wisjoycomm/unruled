using System;
using System.Threading;

namespace Unruled.SimLab
{
    /// <summary>
    /// A fixed set of threads of its own for writing player views while the world thread holds the world lock. It
    /// doesn't use the .NET thread pool: socket handlers park pool threads on that same lock, and pool work would wait
    /// behind them forever. <see cref="For"/> runs a body for 0..count-1 across the workers and the calling thread, and
    /// returns when every index is done.
    /// </summary>
    public sealed class ViewWorkers
    {
        private readonly Thread[] _threads;
        private readonly object _gate = new object();
        private Action<int> _body;
        private int _count;
        private int _next;
        private int _busy; // workers still in the current run
        private long _run; // bumped per For: workers wait for a new one
        private Exception _failure;

        public ViewWorkers(int workers)
        {
            _threads = new Thread[Math.Max(0, workers)];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(Work) { IsBackground = true, Name = "views-" + i };
                _threads[i].Start();
            }
        }

        public int Workers => _threads.Length;

        public void For(int count, Action<int> body)
        {
            if (count <= 0)
                return;
            lock (_gate)
            {
                _body = body;
                _count = count;
                _next = 0;
                _busy = _threads.Length;
                _failure = null;
                _run++;
                Monitor.PulseAll(_gate);
            }
            Drain(body, count);
            lock (_gate)
            {
                while (_busy > 0)
                    Monitor.Wait(_gate);
                _body = null;
                if (_failure != null)
                    throw new AggregateException("A view worker failed.", _failure);
            }
        }

        private void Work()
        {
            long seen = 0;
            while (true)
            {
                Action<int> body;
                int count;
                lock (_gate)
                {
                    while (_run == seen)
                        Monitor.Wait(_gate);
                    seen = _run;
                    body = _body;
                    count = _count;
                }
                try
                {
                    Drain(body, count);
                }
                catch (Exception e)
                {
                    lock (_gate)
                        _failure ??= e;
                }
                lock (_gate)
                {
                    _busy--;
                    if (_busy == 0)
                        Monitor.PulseAll(_gate);
                }
            }
        }

        private void Drain(Action<int> body, int count)
        {
            int i;
            while ((i = Interlocked.Increment(ref _next) - 1) < count)
                body(i);
        }
    }
}
