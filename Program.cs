using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

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
                    var runner = serviceProvider.GetRequiredService<Runner>();
                    runner.DoAction();

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
                .AddTransient<Runner>()
                .AddTransient<SingleThreadSynchronizationContext>()
                .AddLogging(loggingBuilder => {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    loggingBuilder.AddNLog(config);
                })
                .BuildServiceProvider();
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

                var t = DemoAsync();
                t.ContinueWith(_ => {
                    _logger.LogDebug("In continuation");
                    _syncContext.Complete();
                    _syncContext.RunOnCurrentThread();
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
                _logger.LogDebug("DemoAsync b4 yield {0}", i);
                await Task.Yield();
                _logger.LogDebug("DemoAsync aft yield {0}", i);
            }

            foreach(var pair in d) 
                Console.WriteLine(pair);
        }

    }
    class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly ILogger<SingleThreadSynchronizationContext> _logger;
        
        public SingleThreadSynchronizationContext(ILogger<SingleThreadSynchronizationContext> logger)
        {
            _logger = logger;
        }
        private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, Object>> _workItemQueue
            = new BlockingCollection<KeyValuePair<SendOrPostCallback, object>>();
        
        public override void Send(SendOrPostCallback d, object state) 
        {
            throw new NotSupportedException("Send is not supported in this sync context");
        }

        public override void Post(SendOrPostCallback d, object state) 
        {
            _workItemQueue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
            _logger.LogDebug("Post, work queue has {0} items", _workItemQueue.Count);
        }

        public void RunOnCurrentThread()
        {
            _logger.LogDebug("Run {0} items in work queue.", _workItemQueue.Count);
            foreach (var workItem in _workItemQueue.GetConsumingEnumerable())
                workItem.Key(workItem.Value);
        }

        public void Complete() 
        { 
            _logger.LogDebug("Complete, work queue has {0} items", _workItemQueue.Count);
            _workItemQueue.CompleteAdding();
        }
    }
}
