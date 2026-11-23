// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Application.Tests.Architecture;

public class DependencyTests
{
    // Core assemblies may use the framework and, for Application, the inner layer. Anything else (a rendering
    // engine, a third-party package, an outer Nerdcraft project) is a layering violation.
    [Theory]
    [InlineData(typeof(BlockId), new string[0])]
    [InlineData(typeof(NerdcraftGame), new[] { "Nerdcraft.Domain" })]
    public void CoreAssemblies_ReferenceOnlyFrameworkAndInnerLayers(Type typeInAssembly, string[] allowedNerdcraft)
    {
        string[] unexpected = typeInAssembly.Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !IsFramework(name) && !allowedNerdcraft.Contains(name))
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public async Task ProductionProjects_DeclareOnlyAllowedDirectDependencies()
    {
        string root = FindRepositoryRoot();
        string sourceDirectory = Path.Combine(root, "src");
        Dictionary<string, (string[] Projects, string[] Packages)> allowed = new()
        {
            [ProjectPath("Nerdcraft.Domain")] = ([], []),
            [ProjectPath("Nerdcraft.Application")] = ([ProjectPath("Nerdcraft.Domain")], []),
            [ProjectPath("Nerdcraft.Rendering.Raylib")] = ([ProjectPath("Nerdcraft.Application")], ["Raylib-cs"]),
            [ProjectPath("Nerdcraft.Desktop")] = ([ProjectPath("Nerdcraft.Application"), ProjectPath("Nerdcraft.Rendering.Raylib")], []),
        };

        string[] projects = Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceDirectory, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(allowed.Keys.Order(StringComparer.Ordinal), projects);

        foreach (string project in projects)
        {
            (string[] allowedProjects, string[] allowedPackages) = allowed[project];
            using JsonDocument evaluation = await EvaluateProjectAsync(root, Path.Combine(sourceDirectory, project));
            JsonElement items = evaluation.RootElement.GetProperty("Items");
            foreach (JsonElement item in items.GetProperty("ProjectReference").EnumerateArray())
            {
                string fullPath = item.GetProperty("FullPath").GetString() ?? string.Empty;
                string reference = Path.GetRelativePath(sourceDirectory, fullPath);
                Assert.True(allowedProjects.Contains(reference, StringComparer.Ordinal), $"{project} references {reference}.");
            }

            foreach (JsonElement item in items.GetProperty("PackageReference").EnumerateArray())
            {
                string package = item.GetProperty("Identity").GetString() ?? string.Empty;
                Assert.True(allowedPackages.Contains(package, StringComparer.OrdinalIgnoreCase), $"{project} references package {package}.");
            }
        }
    }

    private static string ProjectPath(string name) => Path.Combine(name, name + ".csproj");

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nerdcraft.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Could not find Nerdcraft.slnx above {AppContext.BaseDirectory}.");
    }

    private static async Task<JsonDocument> EvaluateProjectAsync(string root, string project)
    {
        string configuration = typeof(DependencyTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "msbuild", project, "-getItem:ProjectReference,PackageReference", $"-property:Configuration={configuration}" },
        };
        using Process process = new() { StartInfo = start };
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        process.Start();
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            string result = await output;
            string error = await errors;
            Assert.True(process.ExitCode == 0, $"MSBuild evaluation failed for {project}: {error}{result}");
            return JsonDocument.Parse(result);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static bool IsFramework(string name)
        => name is "System" or "netstandard" or "mscorlib"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal);
}
