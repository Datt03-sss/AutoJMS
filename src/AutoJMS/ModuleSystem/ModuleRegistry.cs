using AutoJMS.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.ModuleSystem
{
    public class ModuleRegistry
    {
        private readonly List<(PluginLoadContext Context, IPlugin Module)> _loadedModules = new();
        private readonly object _lock = new();

        public IRetryPolicy Retry { get; private set; }
        public IWorkflowProvider Workflow { get; private set; }
        public ISelectorProvider Selectors { get; private set; }
        public IConfigProvider Config { get; private set; }

        public void RegisterBuiltIn()
        {
            Retry ??= new BuiltInRetryPolicy();
            Workflow ??= new BuiltInWorkflowProvider();
            Selectors ??= new BuiltInSelectorProvider();
            Config ??= new BuiltInConfigProvider();
        }

        // ─── Load from Active Pointer ────────────────────────

        public async Task LoadFromActivePointerAsync(CancellationToken ct = default)
        {
            var active = ActiveModules.LoadLocal();
            var localModules = LoadModulesCache();

            foreach (var kv in active.Modules)
            {
                var moduleName = kv.Key;
                var version = kv.Value;
                var entry = localModules?.GetModule(moduleName);
                if (entry == null) continue;

                var fileName = Path.GetFileName(entry.File ?? entry.Name);

                // Skip non-DLL modules (JSON data files handled by IConfigProvider/ISelectorProvider)
                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Info($"Module {moduleName} is a data file, not a plugin");
                    continue;
                }

                var filePath = active.GetModuleFilePath(moduleName, fileName);
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    AppLogger.Warning($"Module {moduleName} v{version} not found at {filePath}");
                    continue;
                }

                await LoadSingleModuleAsync(moduleName, filePath, entry, active, ct);
            }
        }

        private async Task LoadSingleModuleAsync(
            string moduleName, string filePath, ModuleEntry entry,
            ActiveModules active, CancellationToken ct)
        {
            try
            {
                var ctx = new PluginLoadContext(filePath);
                var plugin = ctx.CreateInstance<IPlugin>();
                if (plugin == null)
                {
                    AppLogger.Warning($"No IPlugin found in {moduleName}");
                    ctx.Unload();
                    return;
                }

                var ok = await plugin.InitializeAsync(ct);
                if (!ok)
                {
                    AppLogger.Error($"Module {moduleName} initialize failed, attempting rollback");
                    ctx.Unload();
                    await RollbackModule(moduleName, entry, active);
                    return;
                }

                lock (_lock)
                {
                    _loadedModules.Add((ctx, plugin));
                }
                MapInterfaces(plugin);
                AppLogger.Info($"Loaded module: {plugin.Name} v{plugin.Version}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load module {moduleName}", ex);
                await RollbackModule(moduleName, entry, active);
            }
        }

        // ─── Rollback ────────────────────────────────────────

        private async Task RollbackModule(string moduleName, ModuleEntry currentEntry, ActiveModules active)
        {
            var currentVersion = currentEntry?.Version;
            if (string.IsNullOrWhiteSpace(currentVersion)) return;

            var versionsDir = Path.Combine(AppPaths.ModulesCacheDir, "modules", moduleName);
            if (!Directory.Exists(versionsDir)) return;

            var versions = Directory.GetDirectories(versionsDir)
                .Select(Path.GetFileName)
                .Where(v => v != currentVersion)
                .OrderByDescending(v => v)
                .ToList();

            foreach (var prevVersion in versions)
            {
                AppLogger.Info($"Rolling back {moduleName} to v{prevVersion}...");
                active.SetActiveVersion(moduleName, prevVersion);
                active.SaveLocal();

                var prevFile = currentEntry != null ? Path.GetFileName(currentEntry.File) : $"{moduleName}.dll";

                var prevPath = active.GetModuleFilePath(moduleName, prevFile);
                if (string.IsNullOrWhiteSpace(prevPath) || !File.Exists(prevPath)) continue;

                try
                {
                    var ctx = new PluginLoadContext(prevPath);
                    var plugin = ctx.CreateInstance<IPlugin>();
                    if (plugin == null) { ctx.Unload(); continue; }

                    var ok = await plugin.InitializeAsync(ct: default);
                    if (ok)
                    {
                        lock (_lock)
                        {
                            _loadedModules.Add((ctx, plugin));
                        }
                        MapInterfaces(plugin);
                        AppLogger.Info($"Rollback successful: {moduleName} v{prevVersion}");
                        return;
                    }
                    ctx.Unload();
                }
                catch
                {
                    // try next version
                }
            }

            AppLogger.Warning($"No working version found for {moduleName}, using built-in fallback");
        }

        // ─── Interface Mapping ────────────────────────────────

        private void MapInterfaces(IPlugin plugin)
        {
            if (plugin is IRetryPolicy r) Retry = r;
            if (plugin is IWorkflowProvider w) Workflow = w;
            if (plugin is ISelectorProvider s) Selectors = s;
            if (plugin is IConfigProvider c) Config = c;
        }

        // ─── Legacy single-module loader (for backward compat) ─

        public async Task<bool> LoadModuleAsync(string filePath, CancellationToken ct = default)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                var ctx = new PluginLoadContext(filePath);
                var plugin = ctx.CreateInstance<IPlugin>();
                if (plugin == null) { ctx.Unload(); return false; }

                var ok = await plugin.InitializeAsync(ct);
                if (!ok) { ctx.Unload(); return false; }

                lock (_lock) { _loadedModules.Add((ctx, plugin)); }
                MapInterfaces(plugin);
                AppLogger.Info($"Loaded module: {plugin.Name} v{plugin.Version}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load module from {filePath}", ex);
                return false;
            }
        }

        // ─── Utilities ───────────────────────────────────────

        public bool IsLoaded<T>() where T : class
        {
            lock (_lock)
                return _loadedModules.Any(m => m.Module is T);
        }

        public void UnloadAll()
        {
            lock (_lock)
            {
                foreach (var (ctx, _) in _loadedModules)
                {
                    try { ctx.Unload(); } catch { }
                }
                _loadedModules.Clear();
            }
        }

        private static ModulesManifest LoadModulesCache()
        {
            try
            {
                var path = Path.Combine(AppPaths.ModulesCacheDir, "modules", "modules-cache.json");
                if (File.Exists(path))
                    return ModulesManifest.FromJson(File.ReadAllText(path));
            }
            catch { }
            return null;
        }

        public int LoadedCount
        {
            get { lock (_lock) return _loadedModules.Count; }
        }
    }
}
