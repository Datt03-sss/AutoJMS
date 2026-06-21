using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Tests.Helpers;

public sealed class StaTaskScheduler : TaskScheduler, IDisposable
{
    private readonly BlockingCollection<Task> _tasks = new();
    private readonly Thread[] _threads;

    public StaTaskScheduler(int numberOfThreads)
    {
        if (numberOfThreads < 1) throw new ArgumentOutOfRangeException(nameof(numberOfThreads));

        _threads = new Thread[numberOfThreads];
        for (int i = 0; i < numberOfThreads; i++)
        {
            _threads[i] = new Thread(() =>
            {
                foreach (var task in _tasks.GetConsumingEnumerable())
                {
                    TryExecuteTask(task);
                }
            })
            {
                IsBackground = true
            };
            _threads[i].SetApartmentState(ApartmentState.STA);
            _threads[i].Start();
        }
    }

    protected override void QueueTask(Task task)
    {
        _tasks.Add(task);
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && TryExecuteTask(task);
    }

    protected override IEnumerable<Task> GetScheduledTasks()
    {
        return _tasks;
    }

    public override int MaximumConcurrencyLevel => _threads.Length;

    public void Dispose()
    {
        _tasks.CompleteAdding();
        foreach (var thread in _threads)
        {
            if (thread.IsAlive)
            {
                thread.Join();
            }
        }
        _tasks.Dispose();
    }
}

public static class StaHelper
{
    public static T Run<T>(Func<Task<T>> action)
    {
        using var scheduler = new StaTaskScheduler(1);
        return Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap().GetAwaiter().GetResult();
    }

    public static void Run(Func<Task> action)
    {
        using var scheduler = new StaTaskScheduler(1);
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap().GetAwaiter().GetResult();
    }

    public static void Run(Action action)
    {
        using var scheduler = new StaTaskScheduler(1);
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, scheduler).GetAwaiter().GetResult();
    }
}
