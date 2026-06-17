using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace AutoJMS.ModuleSystem
{
    public sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginPath;
        private readonly HashSet<string> _sharedAssemblies;
        private Assembly _pluginAssembly;

        public Assembly PluginAssembly => _pluginAssembly;

        public PluginLoadContext(string pluginPath)
            : base($"Plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
        {
            _pluginPath = pluginPath;
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _sharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Private.CoreLib",
                "System.Runtime",
                "System.Console",
                "System.Linq",
                "System.Threading.Tasks",
                "System.Collections",
                "System.ComponentModel",
                "System.Net.Http",
                "System.Text.Json",
                "System.Text.RegularExpressions",
                "Microsoft.Win32.Primitives",
                "netstandard"
            };
        }

        public Assembly LoadPlugin()
        {
            if (_pluginAssembly == null)
                _pluginAssembly = LoadFromAssemblyPath(_pluginPath);
            return _pluginAssembly;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != null && _sharedAssemblies.Contains(assemblyName.Name))
                return Default.LoadFromAssemblyName(assemblyName);

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved != null && File.Exists(resolved))
                return LoadFromAssemblyPath(resolved);

            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                return null;
            }
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null && File.Exists(path))
                return LoadUnmanagedDllFromPath(path);
            return IntPtr.Zero;
        }

        public T CreateInstance<T>(string typeName = null) where T : class
        {
            var asm = LoadPlugin();
            if (typeName != null)
            {
                var type = asm.GetType(typeName);
                if (type != null && typeof(T).IsAssignableFrom(type))
                    return Activator.CreateInstance(type) as T;
            }

            foreach (var t in asm.GetExportedTypes())
            {
                if (typeof(T).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                    return Activator.CreateInstance(t) as T;
            }
            return null;
        }

        public IEnumerable<T> CreateAllInstances<T>() where T : class
        {
            var asm = LoadPlugin();
            foreach (var t in asm.GetExportedTypes())
            {
                if (typeof(T).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                {
                    if (Activator.CreateInstance(t) is T instance)
                        yield return instance;
                }
            }
        }
    }
}
