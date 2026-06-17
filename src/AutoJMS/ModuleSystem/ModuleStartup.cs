using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.ModuleSystem
{
    public static class ModuleStartup
    {
        private static readonly string ModulesDir = Path.Combine(AppPaths.ModulesCacheDir, "modules");
        private static SupabaseModuleProvider _provider;
        private static ModuleRegistry _registry;
        private static readonly object _initLock = new();
        private static bool _initialized;

        public static ModuleRegistry Registry
        {
            get
            {
                if (!_initialized)
                {
                    lock (_initLock)
                    {
                        if (!_initialized)
                        {
                            _registry = new ModuleRegistry();
                            _registry.RegisterBuiltIn();
                            _initialized = true;
                        }
                    }
                }
                return _registry;
            }
        }

        public static SupabaseModuleProvider Provider
        {
            get
            {
                if (_provider == null)
                    _provider = new SupabaseModuleProvider();
                return _provider;
            }
        }

        public static Task InitializeAsync()
        {
            Directory.CreateDirectory(ModulesDir);
            var _ = Registry;
            return Task.CompletedTask;
        }

        public static async Task SyncAsync(CancellationToken ct = default)
        {
            await Provider.SyncAsync(ct);
        }

        public static async Task LoadModulesAsync(CancellationToken ct = default)
        {
            var active = ActiveModules.LoadLocal();
            if (active.Modules.Count == 0)
            {
                AppLogger.Info("No active modules, using built-in implementations");
                return;
            }
            await _registry.LoadFromActivePointerAsync(ct);
        }

        public static async Task BootstrapAsync(CancellationToken ct = default)
        {
            await InitializeAsync();
            await LoadModulesAsync(ct);
            // Background sync (called separately from Program.cs)
            AppLogger.Info($"Module system ready (offline). Loaded={_registry.LoadedCount}, " +
                           $"Retry={_registry.Retry?.GetType().Name}, " +
                           $"Workflow={_registry.Workflow?.GetType().Name}, " +
                           $"Selectors={_registry.Selectors?.GetType().Name}, " +
                           $"Config={_registry.Config?.GetType().Name}");
        }
    }
}
