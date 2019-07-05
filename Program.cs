using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using NLog.Extensions.Logging;
using SyncContextExpr.Async;

namespace SyncContextExpr
{
    class Program
    {
        static void Main(string[] args)
        {
            var logger = LogManager.GetCurrentClassLogger();
            try 
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional:true, reloadOnChange: true)
                    .Build();
                var serviceProvider = BuildDi(config);
                using (serviceProvider as IDisposable)
                {
                    Console.WriteLine("Starting runner");
                    var runner = serviceProvider.GetRequiredService<AsyncRunner>();
                    runner.RunAsync(DemoAsync);

                    Console.WriteLine("Press any key ...");
                    Console.ReadKey();
                }
            }
            finally 
            {
                LogManager.Shutdown();
            }
        }
        static IServiceProvider BuildDi(IConfiguration config) 
        {
            return new ServiceCollection()
                .AddTransient<AsyncRunner>()
                .AddLogging(loggingBuilder => {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    loggingBuilder.AddNLog(config);
                })
                .BuildServiceProvider();
        }
        private static async Task DemoAsync() 
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

    public class AsyncRunner 
    {
        private readonly SingleThreadSynchronizationContext _syncContext
            = new SingleThreadSynchronizationContext();

        private ILogger<AsyncRunner> _logger;

        public AsyncRunner() : this(null) {}

        public AsyncRunner(ILogger<AsyncRunner> logger) {
            _logger = logger ?? NullLogger<AsyncRunner>.Instance;
        }
        public void RunAsync(Func<Task> func)
        { 
            RunAsync(func()); 
        }

        public void RunAsync(Task task) 
        {
            var preCtx = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(_syncContext);

                var t = task;
                t.ContinueWith(_ => {
                    _logger.LogDebug("In continuation");
                    _syncContext.Complete();
                }, TaskScheduler.Default);

                _syncContext.RunOnCurrentThread();
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(preCtx);
            }
        }

    }
    internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly ILogger<SingleThreadSynchronizationContext> _logger;
        /// <summary>
        /// Owner thread id.true
        /// 
        /// Owner thread is the thread in which an instance of SingleThreadSynchronizationContext
        /// is created.
        /// </summary>
        /// <value></value>
        public int OwnerThreadId { get; } = Thread.CurrentThread.ManagedThreadId;
        public SingleThreadSynchronizationContext() 
        {
            _logger = NullLogger<SingleThreadSynchronizationContext>.Instance;
        }
        public SingleThreadSynchronizationContext(ILogger<SingleThreadSynchronizationContext> logger)
        {
            _logger = logger ?? NullLogger<SingleThreadSynchronizationContext>.Instance;
        }
        private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, Object>> _workItemQueue
            = new BlockingCollection<KeyValuePair<SendOrPostCallback, object>>();
        
        /// <summary>
        /// Request to execute a delegate synchronously
        /// </summary>
        /// <param name="d">delegate containing code to execute</param>
        /// <param name="state">state object</param>
        public override void Send(SendOrPostCallback d, object state) 
        {
            var callback = d ?? throw new ArgumentNullException(nameof(d));
            if (OwnerThreadId == Thread.CurrentThread.ManagedThreadId)
                callback(state);
            else
                Post(d, state);
        }
        /// <summary>
        /// Request to execute a delegate asynchronously
        /// </summary>
        /// <param name="d">delegate containing code to execute</param>
        /// <param name="state">state object</param>
        /// <remarks>
        /// Caller need to take care not to recursively call this method in the 
        /// <see cref="OwnerThreadId" /> thread, which could cause a stack
        /// overflow.
        /// </remarks>
        public override void Post(SendOrPostCallback d, object state) 
        {
            var callback = d ?? throw new ArgumentNullException(nameof(d));
            if (OwnerThreadId == Thread.CurrentThread.ManagedThreadId)
                callback(state);
            _workItemQueue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
            _logger.LogDebug("Posted, work queue has {0} items", _workItemQueue.Count);
        }
        /// <summary>
        /// Consume work item queued to this context.
        /// </summary>
        /// <exception
        /// <remarks>
        /// This member blocks the calling thread when there is no work item queued until
        /// the <see cref="Complete()" /> is called.
        /// </remarks>
        public void RunOnCurrentThread()
        {
            if (OwnerThreadId != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Must called in owner thread.");

            _logger.LogDebug("Run {0} items in work queue.", _workItemQueue.Count);
            foreach (var workItem in _workItemQueue.GetConsumingEnumerable())
                workItem.Key(workItem.Value);
        }
        /// <summary>
        /// Signal that there will be no more work item any more.true
        /// 
        /// This will cause the owner thread no longer waiting for new work item on 
        /// the queue. The <see cref="RunOnCurrentThread()" /> will return.
        /// </summary>
        public void Complete() 
        { 
            _logger.LogDebug("Complete, work queue has {0} items", _workItemQueue.Count);
            _workItemQueue.CompleteAdding();
        }
    }
}
