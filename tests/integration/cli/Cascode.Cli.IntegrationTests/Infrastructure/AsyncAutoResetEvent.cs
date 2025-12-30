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
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (_signaled)
            {
                _signaled = false;
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(
                    state =>
                    {
                        var source = (TaskCompletionSource)state!;
                        source.TrySetCanceled(cancellationToken);
                    },
                    waiter
                );
            }

            _waiters.Enqueue(waiter);
            return waiter.Task;
        }
    }

    public void Set()
    {
        TaskCompletionSource? toRelease = null;

        while (true)
        {
            lock (_waiters)
            {
                if (_waiters.Count == 0)
                {
                    if (!_signaled)
                    {
                        _signaled = true;
                    }
                    return;
                }

                toRelease = _waiters.Dequeue();
            }

            if (toRelease.TrySetResult())
            {
                return;
            }

            // canceled or already-completed waiter; loop to release the next queued waiter
            toRelease = null;
        }
    }
}
