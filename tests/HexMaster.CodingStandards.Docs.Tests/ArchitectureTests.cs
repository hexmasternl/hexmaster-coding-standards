using System.Reflection;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// The Docs project owns every document concern and must stay reachable from tests without
/// a web host. That only holds while it depends on no ASP.NET Core hosting types, so the
/// dependency direction is asserted rather than left to convention.
/// </summary>
public class ArchitectureTests
{
    private static Assembly DocsAssembly => Assembly.Load("HexMaster.CodingStandards.Docs");

    [Fact]
    public void DocsAssemblyDoesNotReferenceAspNetCore()
    {
        var aspNetCoreReferences = DocsAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true)
            .ToArray();

        aspNetCoreReferences.ShouldBeEmpty();
    }

    [Fact]
    public void DocsAssemblyDoesNotReferenceTheHost()
    {
        var hostReferences = DocsAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.Contains("CodingStandards.Mcp", StringComparison.Ordinal) == true)
            .ToArray();

        hostReferences.ShouldBeEmpty();
    }
}
