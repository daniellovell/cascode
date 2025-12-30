using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cascode.Cli;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class ShellStateTests
{
    [Fact]
    public async Task AddMessage_FromBackgroundThread_TriggersChangedEventImmediately()
    {
        // Arrange
        var state = new ShellState("/test/workspace");
        var messagesReceived = new ConcurrentQueue<(DateTime timestamp, string message)>();
        var firstMessageReceived = new TaskCompletionSource<bool>();
        var secondMessageReceived = new TaskCompletionSource<bool>();

        state.Changed += () =>
        {
            var snapshot = state.GetMessagesSnapshot();
            if (snapshot.Length > 0)
            {
                var lastMessage = snapshot[^1];
                messagesReceived.Enqueue((DateTime.UtcNow, lastMessage));

                if (messagesReceived.Count == 1)
                {
                    firstMessageReceived.TrySetResult(true);
                }
                else if (messagesReceived.Count == 2)
                {
                    secondMessageReceived.TrySetResult(true);
                }
            }
        };

        var taskStarted = new TaskCompletionSource<bool>();
        var taskShouldComplete = new TaskCompletionSource<bool>();

        // Act - Simulate scan running in background thread
        var backgroundTask = Task.Run(async () =>
        {
            taskStarted.SetResult(true);
            state.AddMessage("Message 1 - scan started");

            await Task.Delay(50); // Simulate some work
            state.AddMessage("Message 2 - progress");

            // Wait here - task is NOT complete yet
            await taskShouldComplete.Task;
            state.AddMessage("Message 3 - completed");
        });

        // Wait for task to start
        await taskStarted.Task;

        // Assert - Verify messages arrive WHILE task is still running
        var firstReceived = await Task.WhenAny(
            firstMessageReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(2))
        );
        Assert.True(
            firstReceived == firstMessageReceived.Task,
            "First message should be received within timeout"
        );
        Assert.False(
            backgroundTask.IsCompleted,
            "Background task should still be running when first message received"
        );

        var secondReceived = await Task.WhenAny(
            secondMessageReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(2))
        );
        Assert.True(
            secondReceived == secondMessageReceived.Task,
            "Second message should be received within timeout"
        );
        Assert.False(
            backgroundTask.IsCompleted,
            "Background task should still be running when second message received"
        );

        // Let task complete
        taskShouldComplete.SetResult(true);
        await backgroundTask;

        // Verify all messages were received in order
        var receivedList = messagesReceived.ToList();
        Assert.True(
            receivedList.Count >= 3,
            $"Should receive at least 3 messages, got {receivedList.Count}"
        );
        Assert.Contains("Message 1", receivedList[0].message);
        Assert.Contains("Message 2", receivedList[1].message);
    }

    [Fact]
    public async Task AddMessage_FromBackgroundThread_AppearsInSnapshot()
    {
        // Arrange
        var state = new ShellState("/test/workspace");
        var messageAdded = new TaskCompletionSource<bool>();
        var taskShouldComplete = new TaskCompletionSource<bool>();

        state.Changed += () => messageAdded.TrySetResult(true);

        // Act - Add message from background thread
        var backgroundTask = Task.Run(async () =>
        {
            state.AddMessage("Background message");

            // Wait to prove snapshot is available before task completes
            await taskShouldComplete.Task;
        });

        // Assert - Message appears in snapshot while task is still running
        var messageReceived = await Task.WhenAny(
            messageAdded.Task,
            Task.Delay(TimeSpan.FromSeconds(1))
        );
        Assert.True(messageReceived == messageAdded.Task, "Changed event should fire");

        var snapshot = state.GetMessagesSnapshot();
        Assert.NotEmpty(snapshot);
        Assert.Contains("Background message", snapshot[^1]);
        Assert.False(backgroundTask.IsCompleted, "Background task should still be running");

        // Cleanup
        taskShouldComplete.SetResult(true);
        await backgroundTask;
    }

    [Fact]
    public async Task GetMessagesSnapshot_IsThreadSafe()
    {
        // Arrange
        var state = new ShellState("/test/workspace");
        var exceptions = new ConcurrentBag<Exception>();
        var iterations = 100;

        // Act - Concurrent reads and writes
        var tasks = new[]
        {
            // Writer thread
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        state.AddMessage($"Message {i}");
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }),
            // Reader thread 1
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var snapshot = state.GetMessagesSnapshot();
                        _ = snapshot.Length; // Just access it
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }),
            // Reader thread 2
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var snapshot = state.GetMessagesSnapshot();
                        _ = snapshot.Length; // Just access it
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }),
        };

        var allTasks = Task.WhenAll(tasks);
        var completedTask = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(30)));

        // Assert - No exceptions occurred
        Assert.True(completedTask == allTasks, "All tasks should complete within timeout");
        Assert.Empty(exceptions);

        // Verify final state is consistent
        var finalSnapshot = state.GetMessagesSnapshot();
        Assert.True(finalSnapshot.Length > 0);
        Assert.True(finalSnapshot.Length <= iterations);
    }
}
