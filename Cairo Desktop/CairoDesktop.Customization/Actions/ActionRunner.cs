using System;
using System.Diagnostics;
using System.IO;
using CairoDesktop.Application.Interfaces;

namespace CairoDesktop.Customization.Actions
{
    /// <summary>
    /// Runs the actions that hotkeys and widget clicks can trigger:
    ///   launch  - start a program, document or URL (arg = path, args = arguments)
    ///   run     - run a script / command line without a window (arg = command line, via cmd.exe)
    ///   theme   - switch theme pack (arg = pack folder name, "next", or "none")
    ///   command - invoke a built-in Cairo command by name (e.g. ToggleDesktopOverlay, ShowRunDialog)
    ///   reload  - re-apply cairo-plus.json
    /// </summary>
    internal sealed class ActionRunner
    {
        public static readonly string[] Actions = { "launch", "run", "theme", "command", "reload" };

        private readonly ICommandService _commandService;
        private readonly CairoPlusService _service;

        public ActionRunner(ICommandService commandService, CairoPlusService service)
        {
            _commandService = commandService;
            _service = service;
        }

        /// <summary>Checks an action definition without running it. Returns an error or null.</summary>
        public static string Validate(string action, string arg)
        {
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "launch":
                case "run":
                case "command":
                    return string.IsNullOrWhiteSpace(arg) ? $"action '{action}' needs an \"arg\"." : null;
                case "theme":
                case "reload":
                    return null;
                default:
                    return $"unknown action '{action}' (use {string.Join(", ", Actions)}).";
            }
        }

        /// <summary>Runs an action. Returns an error message, or null on success. Call on the UI thread.</summary>
        public string Run(string action, string arg, string args = null)
        {
            string error = Validate(action, arg);
            if (error != null) return error;

            try
            {
                switch (action.Trim().ToLowerInvariant())
                {
                    case "launch":
                        Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(arg.Trim()), args ?? "")
                        {
                            UseShellExecute = true,
                            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                        });
                        return null;

                    case "run":
                        Process.Start(new ProcessStartInfo("cmd.exe", "/d /s /c \"" + Environment.ExpandEnvironmentVariables(arg) + "\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                        });
                        return null;

                    case "theme":
                        if (string.IsNullOrWhiteSpace(arg) || arg.Equals("next", StringComparison.OrdinalIgnoreCase))
                        {
                            _service.ApplyNextTheme();
                        }
                        else
                        {
                            _service.ApplyTheme(arg.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : arg.Trim());
                        }
                        return null;

                    case "command":
                        if (_commandService == null) return "Cairo's command service is unavailable.";
                        return _commandService.InvokeCommand(arg.Trim()) ? null : $"Cairo command '{arg}' was not found or failed.";

                    case "reload":
                        _service.Reload();
                        return null;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException || ex is InvalidOperationException)
            {
                return $"{action} '{arg}': {ex.Message}";
            }

            return null;
        }
    }
}
