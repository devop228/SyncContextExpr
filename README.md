## Purpose ##

1. This project demonstrates using a custom SynchronizationContext `SingleThreadSynchronizationContext`.
   `AsyncRunner.RunAsync(Task)` is the main driver of the demonstration. The method set the current
   sync context to an instance of `SingleThreadSynchronizationContext`. Refer this [OneNote](https://onedrive.live.com/view.aspx?resid=436DFD03018EC53%21355&id=documents&wd=target%28DotNET%20Threading.one%7C467ABCED-1019-4592-AF3B-0C9065B529E3%2FSynchronizationContext%7C78D0BD9F-648C-4639-8196-D3063ADDD71D%2F%29) for detail and other references.

2. Demonstrate usage of dependency injection (DI). Use `BuildDI` method to create `IServiceProvier`
   
3. Use NLog with the established DI, and utilise NullLogger<T> in case of no logger is injected, for example:

    ```
    public SingleThreadSynchronizationContext(ILogger<SingleThreadSynchronizationContext> logger)
    {
        _logger = logger ?? NullLogger<SingleThreadSynchronizationContext>.Instance;
    }
    ```