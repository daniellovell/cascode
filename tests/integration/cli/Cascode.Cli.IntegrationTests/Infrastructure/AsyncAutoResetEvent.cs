using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

internal sealed class AsyncAutoResetEvent
{
    private readonly Queue<TaskCompletionSource> _waiters = new();
    private bool _signaled;

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        lock (_waiters)
        {
            if (_signaled)
            {
                _signaled = false;
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(state =>
                {
                    var source = (TaskCompletionSource)state!;
                    source.TrySetCanceled(cancellationToken);
                }, waiter);
            }

            _waiters.Enqueue(waiter);
            return waiter.Task;
        }
    }

    public void Set()
    {
        TaskCompletionSource? toRelease = null;

        lock (_waiters)
        {
            if (_waiters.Count > 0)
            {
                toRelease = _waiters.Dequeue();
            }
            else if (!_signaled)
            {
                _signaled = true;
            }
        }

        toRelease?.TrySetResult();
    }
}
