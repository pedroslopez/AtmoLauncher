﻿using Gtk;
using Mono.Options;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Interface {
    class Program {
        public static string ExecutableName => Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location);

        public static bool IsUnix {
            get {
                int p = (int)Environment.OSVersion.Platform;
                return (p == 4) || (p == 6) || (p == 128);
            }
        }

        public static void Main(string[] args) {
            var immediateStart = false;
            var update = false;
            var atmoLink = "";
            string atmoLinkLaunchArgs = "";

            if (args.Length > 0) {
                foreach (string arg in args) {
                    if (arg.StartsWith("atmo://")) {
                        atmoLink = arg;
                        break;
                    }
                }
                try {
                    new OptionSet {
                        {"s|start", "Immediately starts the game, without checking for updates.",s => immediateStart = s != null},
                        {"u|update", "Initiates interface, and starts updating.",u => update = u != null},
                        {"a|atmourl=", "",a => atmoLink = a }
                    }.Parse(args);
                } catch (OptionException e) {
                    Console.WriteLine(e.Message);
                    Console.WriteLine("Try `{0} --help` for usage information.", ExecutableName);
                    return;
                }
            }

            // The launcher.bin file is a GZipped file, whose contents are simply:
            //  - 1 line of JSON describing readonly launcher configuration
            //    (such as update server, window title, etc)
            //  - The remainder of the file is the contents of a Gtk.Builder XML file,
            //    which holds the layout definition.
            // IMPORTANT: When updating it, name the new file 'launcher.bin.new'

            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin.new"))) {
                if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin")))
                    File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin"));
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin.new"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin"));
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin.new"));
            }
            
            using (var file = File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.bin")))
            using (var stream = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(stream)) {
                var setup = JsonConvert.DeserializeObject<LauncherSetup>(reader.ReadLine(), new JsonSerializerSettings {
                    DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
                });

                if (atmoLink != "") {
                    atmoLinkLaunchArgs = "standalone \"" + atmoLink + "\"";
                    StartGame(setup, atmoLinkLaunchArgs);
                    return;
                }
                if (immediateStart) {
                    StartGame(setup);
                    return;
                }
                if (update) {
                    Application.Init();
                    var builder = new Builder();
                    builder.AddFromString(reader.ReadToEnd());

                    new Launcher(setup, builder).Initialize();
                    Application.Run();
                } else {
                    RebootCopy();
                }
            }

            if (!IsUnix && isRunningAsAdministrator()) {
                DirectoryInfo dInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                DirectorySecurity dSecurity = dInfo.GetAccessControl();
                dSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                dInfo.SetAccessControl(dSecurity);
            }
        }

        private static void RebootCopy() {
            var selfPath = Assembly.GetEntryAssembly().Location;
            var newPath = Path.ChangeExtension(selfPath, ".old.exe");
            if (File.Exists(newPath)) {
                File.Delete(newPath);
            }
            File.Copy(selfPath, newPath);
            Process.Start(newPath, "--update");
        }

        public static void RebootOrig(bool asAdmin = false) {
            var processInfo = new ProcessStartInfo(GetOrigPath());

            if (asAdmin)
                processInfo.Verb = "runas";

            Process.Start(processInfo);
            Process.GetCurrentProcess().Kill();
        }

        public static string GetOrigPath() {
            var selfPath = Assembly.GetEntryAssembly().Location;
            if (selfPath.Contains("old"))
                return selfPath.Replace("old.exe", "exe");
            else
                return selfPath;
        }

        public static void StartGame(LauncherSetup setup) {
            StartGame(setup, setup.ExecuteArgs);
        }

        public static void StartGame(LauncherSetup setup, string args) {
            var gamePath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                setup.GameFolder,
                setup.GameExecutable
            );

            if (IsUnix) {
                gamePath = Path.Combine(
                Directory.GetParent(Assembly.GetEntryAssembly().Location).Parent.Parent.ToString(),
                setup.GameFolder,
                setup.GameExecutable
                );

                Process.Start(new ProcessStartInfo(
                    "open",
                    "-a '" + gamePath + "' -n --args " + args) { UseShellExecute = false });
            } else
                Process.Start(gamePath, args);

            Process.GetCurrentProcess().Kill();
        }

        public static bool isRunningAsAdministrator() {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent()))
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
