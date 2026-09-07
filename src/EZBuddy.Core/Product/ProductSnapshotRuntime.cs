namespace EZBuddy.Core.Product;

public interface IProductSnapshotCollector
{
    Task<ProductSnapshotBundle> CaptureAsync(CancellationToken cancellationToken = default);
}

public static class ProductSnapshotRuntime
{
    private static IProductSnapshotCollector? _collector;

    public static IProductSnapshotCollector? Collector
    {
        get => Volatile.Read(ref _collector);
        set => Volatile.Write(ref _collector, value);
    }
}
