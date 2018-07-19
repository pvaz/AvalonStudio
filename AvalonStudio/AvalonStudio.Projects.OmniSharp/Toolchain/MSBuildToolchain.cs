using AvalonStudio.CommandLineTools;
using AvalonStudio.Extensibility.Projects;
using AvalonStudio.Platforms;
using AvalonStudio.Projects;
using AvalonStudio.Projects.OmniSharp;
using AvalonStudio.Projects.OmniSharp.Toolchain;
using AvalonStudio.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvalonStudio.Toolchains.MSBuild
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// Flattens an object hierarchy.
        /// </summary>
        /// <param name="rootLevel">The root level in the hierarchy.</param>
        /// <param name="nextLevel">A function that returns the next level below a given item.</param>
        /// <returns><![CDATA[An IEnumerable<T> containing every item from every level in the hierarchy.]]></returns>
        public static IEnumerable<T> Flatten<T>(this IEnumerable<T> rootLevel, Func<T, IEnumerable<T>> nextLevel)
        {
            HashSet<T> accumulation = new HashSet<T>();

            flattenLevel<T>(accumulation, rootLevel, nextLevel);

            foreach (var item in rootLevel)
            {
                accumulation.Add(item);
            }

            return accumulation;
        }

        /// <summary>
        /// Recursive helper method that traverses a hierarchy, accumulating items along the way.
        /// </summary>
        /// <param name="accumulation">A collection in which to accumulate items.</param>
        /// <param name="currentLevel">The current level we are traversing.</param>
        /// <param name="nextLevel">A function that returns the next level below a given item.</param>
        private static void flattenLevel<T>(HashSet<T> accumulation, IEnumerable<T> currentLevel, Func<T, IEnumerable<T>> nextLevel)
        {
            foreach (T item in currentLevel)
            {
                flattenLevel<T>(accumulation, nextLevel(item), nextLevel);

                foreach (var child in currentLevel)
                {
                    accumulation.Add(child);
                }
            }
        }
    }

    [ExportToolchain]
    [Shared]
    public class MSBuildToolchain : IToolchain
    {
        public string BinDirectory => Path.Combine(Platform.ReposDirectory, "AvalonStudio.Languages.CSharp", "coreclr");

        public string Description => "Toolchain for MSBuild 15 (Dotnet Core Support)";

        public string Name => "MSBuild Toolchain";

        public Version Version => new Version(0, 0, 0);

        private static bool RequiresBuilding(IProject project)
        {
            bool requiresBuild = false;

            if (project.Executable != null)
            {
                if (!File.Exists(project.Executable))
                {
                    requiresBuild = true;
                }
                else
                {
                    var lastBuildDate = File.GetLastWriteTime(project.Executable);

                    if (File.GetLastWriteTime(project.Location) > lastBuildDate)
                    {
                        requiresBuild = true;
                    }
                    else
                    {
                        foreach (var dependency in project.References)
                        {
                            // TODO implement public API analysis to determine if we need to rebuild or not.

                            if (File.GetLastWriteTime(dependency.Executable) > lastBuildDate)
                            {
                                requiresBuild = true;
                                break;
                            }
                        }

                        if (!requiresBuild)
                        {
                            foreach (var file in project.SourceFiles)
                            {
                                if (File.GetLastWriteTime(file.Location) > lastBuildDate)
                                {
                                    requiresBuild = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // TODO unsure about this case, or if its possible?
                requiresBuild = true;
            }

            return requiresBuild;
        }

        private List<Task<(bool result, IProject project)>> QueueItems(List<IProject> toBuild, List<IProject> built, BuildRunner runner)
        {
            var tasks = new List<Task<(bool result, IProject project)>>();

            var toRemove = new List<IProject>();

            foreach (var item in toBuild)
            {
                bool canBuild = true;

                foreach (var dep in item.References.OfType<OmniSharpProject>())
                {
                    if (!built.Contains(dep))
                    {
                        canBuild = false;
                        break;
                    }
                }

                if (canBuild)
                {
                    toRemove.Add(item);
                    tasks.Add(runner.Queue(item));
                }
            }

            foreach (var item in toRemove)
            {
                toBuild.Remove(item);
            }

            return tasks;
        }

        public async Task<bool> BuildAsync(IConsole console, IProject project, string label = "", IEnumerable<string> definitions = null)
        {
            return await Task.Factory.StartNew(() =>
            {
                var exitCode = PlatformSupport.ExecuteShellCommand(DotNetCliService.Instance.Info.Executable, $"build {Path.GetFileName(project.Location)}", (s, e) =>
                {
                    console.WriteLine(e.Data);
                }, (s, e) =>
                {
                    if (e.Data != null)
                    {
                        console.WriteLine();
                        console.WriteLine(e.Data);
                    }
                },
                false, project.CurrentDirectory, false);

                return exitCode == 0;
            });
        }

        public bool CanHandle(IProject project)
        {
            return project is OmniSharpProject;
        }

        public async Task Clean(IConsole console, IProject project)
        {
            await Task.Factory.StartNew(() =>
            {
                console.Write($"Cleaning Project: {project.Name}...");
                var exitCode = PlatformSupport.ExecuteShellCommand(DotNetCliService.Instance.DotNetPath, "clean /nologo", (s, e) => console.WriteLine(e.Data), (s, e) =>
                {
                    if (e.Data != null)
                    {
                        console.WriteLine();
                        console.WriteLine(e.Data);
                    }
                },
                false, project.CurrentDirectory, false);
            });

            console.WriteLine("Done.");
        }

        public IList<object> GetConfigurationPages(IProject project)
        {
            return new List<object>() { };
        }

        public IEnumerable<string> GetToolchainIncludes(ISourceFile file)
        {
            return new List<string>();
        }

        public Task<bool> InstallAsync(IConsole console, IProject project)
        {
            return Task.FromResult(true);
        }

        public void ProvisionSettings(IProject project)
        {
        }
    }
}