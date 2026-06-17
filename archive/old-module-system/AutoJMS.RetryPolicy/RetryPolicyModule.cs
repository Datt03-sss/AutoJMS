using System;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Abstractions;

namespace AutoJMS.RetryPolicy
{
    public class RetryPolicyModule : IModule, IRetryPolicy
    {
        public string Name => "RetryPolicy";
        public string Version => "1.0.0";

        public Task<bool> InitializeAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries, int delayMs, CancellationToken ct = default)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    return await action();
                }
                catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
                {
                    await Task.Delay(delayMs * attempt, ct);
                }
            }
            return await action();
        }
    }
}
