using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Cascode.Cli;
using Cascode.TestSupport;
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
        var firstMessageReceived = new AsyncSignal();
        var secondMessageReceived = new AsyncSignal();

        state.Changed += () =>
        {
            var snapshot = state.GetMessagesSnapshot();
            if (snapshot.Length > 0)
            {
                var lastMessage = snapshot[^1];
                messagesReceived.Enqueue((DateTime.UtcNow, lastMessage));

                if (messagesReceived.Count == 1)
                {
                    firstMessageReceived.TrySet();
                }
                else if (messagesReceived.Count == 2)
                {
                    secondMessageReceived.TrySet();
                }
            }
        };

        var taskStarted = new AsyncSignal();
        var taskShouldComplete = new AsyncSignal();

        // Act - Simulate scan running in background thread
        var backgroundTask = AsyncTest.RunLongRunning(async () =>
        {
            taskStarted.TrySet();
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
        await AsyncTest.WaitAsync(
            firstMessageReceived.Task,
            TimeSpan.FromSeconds(2),
            "First message should be received within timeout"
        );
        Assert.False(
            backgroundTask.IsCompleted,
            "Background task should still be running when first message received"
        );

        await AsyncTest.WaitAsync(
            secondMessageReceived.Task,
            TimeSpan.FromSeconds(2),
            "Second message should be received within timeout"
        );
        Assert.False(
            backgroundTask.IsCompleted,
            "Background task should still be running when second message received"
        );

        // Let task complete
        taskShouldComplete.TrySet();
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
        var messageAdded = new AsyncSignal();
        var taskStarted = new AsyncSignal();
        var taskShouldComplete = new AsyncSignal();

        state.Changed += () => messageAdded.TrySet();

        // Act - Add message from background thread
        var backgroundTask = AsyncTest.RunLongRunning(async () =>
        {
            taskStarted.TrySet();
            state.AddMessage("Background message");

            // Wait to prove snapshot is available before task completes
            await taskShouldComplete.Task;
        });

        await taskStarted.Task;

        // Assert - Message appears in snapshot while task is still running
        await AsyncTest.WaitAsync(
            messageAdded.Task,
            TimeSpan.FromSeconds(2),
            "Changed event should fire"
        );

        var snapshot = state.GetMessagesSnapshot();
        Assert.NotEmpty(snapshot);
        Assert.Contains("Background message", snapshot[^1]);
        Assert.False(backgroundTask.IsCompleted, "Background task should still be running");

        // Cleanup
        taskShouldComplete.TrySet();
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
        await AsyncTest.WaitAsync(
            allTasks,
            TimeSpan.FromSeconds(30),
            "All tasks should complete within timeout"
        );

        // Assert - No exceptions occurred
        Assert.Empty(exceptions);

        // Verify final state is consistent
        var finalSnapshot = state.GetMessagesSnapshot();
        Assert.True(finalSnapshot.Length > 0);
        Assert.True(finalSnapshot.Length <= iterations);
    }
}
