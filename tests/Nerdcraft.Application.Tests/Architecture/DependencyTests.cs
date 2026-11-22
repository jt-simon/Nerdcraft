// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;

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

    private static bool IsFramework(string name)
        => name is "System" or "netstandard" or "mscorlib"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal);
}
