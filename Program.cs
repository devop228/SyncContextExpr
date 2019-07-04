using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SyncContextExpr
{
    class Program
    {
        static async Task Main(string[] args)
        {
        }

    }

    class Runner 
    {
        private SingleThreadSynchronizationContext _syncContext;
        private ILogger<Runner> _logger;
        public Runner(SingleThreadSynchronizationContext syncContext, 
                      ILogger<Runner> logger) {
            _syncContext = syncContext;
            _logger = logger;
        }
        public void DoAction() 
        {
            var preCtx = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(_syncContext);

                var t = DemoAsync().ContinueWith(_ => {
                    _syncContext.Complete();
                }, TaskScheduler.Default);

                _syncContext.RunOnCurrentThread();
                t.GetAwaiter().GetResult();
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(preCtx);
            }
        }
        private async Task DemoAsync() 
        {
            var d = new Dictionary<int, int>();
            for (int i=0; i < 10_000; i++) 
            {
                int id = Thread.CurrentThread.ManagedThreadId;
                int count = 0;
                d[id] = d.TryGetValue(id, out count) ? count+1 : 1;

                await Task.Yield();
            }

            foreach(var pair in d) 
                Console.WriteLine(pair);
        }

    }
    class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, Object>>
            _workItemQueue = new BlockingCollection<KeyValuePair<SendOrPostCallback, object>>();
        
        public override void Send(SendOrPostCallback d, object state) {
            throw new NotSupportedException("Send is not supported in this sync context");
        }

        public override void Post(SendOrPostCallback d, object state) 
        {
            _workItemQueue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
        }

        public void RunOnCurrentThread()
        {
            while (_workItemQueue.TryTake(out KeyValuePair<SendOrPostCallback, object> workItem, Timeout.Infinite))
                workItem.Key(workItem.Value);
        }

        public void Complete() => _workItemQueue.CompleteAdding();
    }
}
