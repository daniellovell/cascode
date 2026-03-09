using System.Threading.Tasks;

namespace Cascode.TestSupport;

internal sealed class AsyncSignal
{
    private readonly TaskCompletionSource<bool> _source = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public Task Task => _source.Task;

    public bool TrySet() => _source.TrySetResult(true);
}
