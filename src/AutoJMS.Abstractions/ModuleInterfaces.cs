using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Abstractions
{
    public interface IPlugin
    {
        string Name { get; }
        string Version { get; }
        Task<bool> InitializeAsync(CancellationToken ct = default);
    }

    public interface IModule : IPlugin
    {
    }

    public interface IRetryPolicy : IPlugin
    {
        Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries, int delayMs, CancellationToken ct = default);
    }

    public interface IWorkflowProvider : IPlugin
    {
        Task<bool> ExecuteAsync(string action, string parameter, CancellationToken ct = default);
        Task<string> GetTrackingDataAsync(string waybillNo, int timeoutMs = 10000);
        bool NeedSwitchToDkch2(string pageSource);
    }

    public interface ISelectorProvider : IPlugin
    {
        string GetSelector(string scope, string key);
        System.Collections.Generic.Dictionary<string, string> GetSelectors(string scope);
    }

    public interface IConfigProvider : IPlugin
    {
        string GetConfig(string section, string key, string defaultValue = "");
        T GetConfig<T>(string section, string key, T defaultValue = default);
        T GetValue<T>(string key, T defaultValue = default);
        System.Collections.Generic.Dictionary<string, object> GetSection(string section);
    }
}
