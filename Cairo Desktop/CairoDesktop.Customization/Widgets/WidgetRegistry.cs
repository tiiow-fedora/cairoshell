using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Widgets
{
    /// <summary>
    /// Knows every widget type: the built-ins plus any ICairoWidgetFactory found in DLLs under
    /// &lt;Cairo&gt;\Widgets or %LOCALAPPDATA%\Cairo Desktop\Widgets. DLLs are loaded once at startup
    /// (.NET can't unload them), so adding a new widget DLL needs a Cairo restart; changing its
    /// options in cairo-plus.json does not.
    /// </summary>
    internal sealed class WidgetRegistry
    {
        private readonly Dictionary<string, ICairoWidgetFactory> _factories = new Dictionary<string, ICairoWidgetFactory>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _loadProblems = new List<string>();

        public IReadOnlyList<string> LoadProblems => _loadProblems;

        public IEnumerable<string> Types => _factories.Keys.OrderBy(k => k);

        public WidgetRegistry()
        {
            foreach (var factory in new ICairoWidgetFactory[]
            {
                new ClockWidgetFactory(),
                new CpuWidgetFactory(),
                new MemoryWidgetFactory(),
                new BatteryWidgetFactory(),
                new NetworkWidgetFactory(),
                new CommandWidgetFactory()
            })
            {
                _factories[factory.Type] = factory;
            }
        }

        public void LoadExternal(IEnumerable<string> folders)
        {
            foreach (string folder in folders.Where(Directory.Exists))
            {
                foreach (string dll in Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(dll);
                    if (name.Equals("CairoDesktop.Widgets.Sdk.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // a copy of the SDK shipped next to a widget; the shell's own copy is used
                    }

                    try
                    {
                        var assembly = Assembly.LoadFrom(dll);
                        var types = assembly.GetExportedTypes()
                            .Where(t => typeof(ICairoWidgetFactory).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);

                        foreach (var type in types)
                        {
                            var factory = (ICairoWidgetFactory)Activator.CreateInstance(type);
                            if (string.IsNullOrWhiteSpace(factory.Type))
                            {
                                _loadProblems.Add($"Widget {type.FullName} in {name} has no Type.");
                            }
                            else if (_factories.ContainsKey(factory.Type))
                            {
                                _loadProblems.Add($"Widget type '{factory.Type}' from {name} is already defined; ignored.");
                            }
                            else
                            {
                                _factories[factory.Type] = factory;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        _loadProblems.Add($"Widget DLL {name}: {ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message}");
                    }
                    catch (BadImageFormatException)
                    {
                        // Native dependency of a widget; not a .NET assembly.
                    }
                    catch (Exception ex)
                    {
                        _loadProblems.Add($"Widget DLL {name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Creates the widget with this id. Its type comes from the options' "type", or the id itself
        /// (so "widget:cpu" works without any options).
        /// </summary>
        public ICairoWidget Create(string id, IDictionary<string, JsonElement> definitions, WidgetBar bar, Dispatcher dispatcher, Action<string> log, out string error)
        {
            error = null;
            JsonElement? options = null;
            if (definitions != null && definitions.TryGetValue(id, out JsonElement def))
            {
                options = def;
            }

            var wrapped = new JsonWidgetOptions(options);
            string type = wrapped.GetString("type", id);

            if (!_factories.TryGetValue(type, out ICairoWidgetFactory factory))
            {
                error = options == null
                    ? $"widget '{id}' is not defined in \"widgets\" and is not a widget type (known: {string.Join(", ", Types)})."
                    : $"widget '{id}' has unknown type '{type}' (known: {string.Join(", ", Types)}).";
                return null;
            }

            try
            {
                return factory.Create(new WidgetContext(id, bar, wrapped, dispatcher, log));
            }
            catch (Exception ex)
            {
                error = $"widget '{id}' failed to start: {ex.Message}";
                return null;
            }
        }
    }
}
