using System.Threading.Tasks;

namespace Cascode.TestSupport;

internal sealed class AsyncSignal<T>
{
    private readonly TaskCompletionSource<T> _source = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public Task<T> Task => _source.Task;

    public bool TrySet(T value) => _source.TrySetResult(value);
}
