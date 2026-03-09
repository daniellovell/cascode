using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cascode.TestSupport;

internal static class AsyncTest
{
    public static Task RunLongRunning(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Task
            .Factory.StartNew(
                work,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            )
            .Unwrap();
    }

    public static async Task WaitAsync(
        Task task,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(task);
        ValidateTimeout(timeout);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        try
        {
            await task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
            when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(failureMessage);
        }
    }

    public static async Task<T> WaitAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(task);
        ValidateTimeout(timeout);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        try
        {
            return await task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
            when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(failureMessage);
        }
    }

    public static Task EventuallyAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        TimeSpan pollInterval,
        string failureMessage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return EventuallyAsync(
            () => Task.FromResult(predicate()),
            timeout,
            pollInterval,
            failureMessage,
            cancellationToken
        );
    }

    public static async Task EventuallyAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        TimeSpan pollInterval,
        string failureMessage,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ValidateTimeout(timeout);
        ValidatePollInterval(pollInterval);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        while (true)
        {
            linked.Token.ThrowIfCancellationRequested();

            if (await predicate())
            {
                return;
            }

            try
            {
                await Task.Delay(pollInterval, linked.Token);
            }
            catch (OperationCanceledException)
                when (timeoutCts.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested
                )
            {
                throw new TimeoutException(failureMessage);
            }
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    private static void ValidatePollInterval(TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }
}
