using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;
using Trinity.Encore.Framework.Core.Exceptions;
using Trinity.Encore.Framework.Core.Runtime;
using Trinity.Encore.Framework.Core.Security;

namespace Trinity.Encore.Framework.Core.Threading.Actors
{
    public abstract class Actor : RestrictedObject, IActor
    {
        private Thread _schedulingThread;

        private IEnumerator<Operation> _msgIterator;

        private IEnumerator<Operation> _mainIterator;

        private readonly ConcurrentQueue<Action> _msgQueue = new ConcurrentQueue<Action>();

        private AutoResetEvent _disposeEvent;

        public bool IsDisposed { get; private set; }

        internal bool IsActive { get; set; }

        internal Scheduler Scheduler { get; set; }

        [ContractInvariantMethod]
        private void Invariant()
        {
            Contract.Invariant(_msgQueue != null);
            Contract.Invariant(_disposeEvent != null);
        }

        private void Setup()
        {
            _disposeEvent = new AutoResetEvent(false);
            Start();
        }

        internal Actor(Scheduler scheduler)
        {
            Contract.Requires(scheduler != null);

            Scheduler = scheduler;

            Setup();
        }

        protected Actor()
        {
            Setup();
        }

        ~Actor()
        {
            Dispose(false);
        }

        public void Join()
        {
            _disposeEvent.WaitOne();
        }

        public void Dispose()
        {
            Post(InternalDispose);
        }

        private void InternalDispose()
        {
            if (IsDisposed)
                return;

            Dispose(true);
            IsDisposed = true;
            GC.SuppressFinalize(this);

            _disposeEvent.Set();
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        private void Start()
        {
            var currentThread = Thread.CurrentThread;
            var oldThread = Interlocked.Exchange(ref _schedulingThread, currentThread);

            if (oldThread != null && oldThread != currentThread)
                throw new InvalidOperationException("An actor cannot be rescheduled within a different scheduler/thread.");

            if (_msgIterator != null)
                _msgIterator.Dispose();

            _msgIterator = EnumerateMessages();

            if (_mainIterator != null)
                _mainIterator.Dispose();

            _mainIterator = Main();

            if (Scheduler == null)
                Scheduler = ActorManager.RegisterActor(this);
        }

        internal bool ProcessMessages()
        {
            _msgIterator.MoveNext();

            return _msgQueue.Count > 0;
        }

        internal bool ProcessMain()
        {
            var result = _mainIterator.MoveNext();

            // Happens if a yield break occurs.
            if (!result)
                return false;

            var operation = _mainIterator.Current;

            if (operation == Operation.Dispose)
                Dispose();

            return operation == Operation.Continue;
        }

        public void Post(Action msg)
        {
            _msgQueue.Enqueue(msg);

            Action tmp;
            while (!_msgQueue.TryPeek(out tmp))
                if (_msgQueue.Count == 0)
                    return; // The message was processed immediately, and we can just return.

            if (msg == tmp)
                Scheduler.AddActor(this); // The message was sent while the actor was idle; restart it to continue processing.
        }

        protected virtual IEnumerator<Operation> Main()
        {
            yield break; // No main by default.
        }

        private IEnumerator<Operation> EnumerateMessages()
        {
            while (true)
            {
                Action msg;
                if (_msgQueue.TryDequeue(out msg))
                {
                    var op = OnMessage(msg);
                    if (op != null)
                        yield return op.Value;
                }

                yield return Operation.Continue;
            }
        }

        protected virtual Operation? OnMessage(Action msg)
        {
            try
            {
                msg();
            }
            catch (Exception ex)
            {
                ExceptionManager.RegisterException(ex);
                return Operation.Dispose;
            }

            return null;
        }
    }
}
