using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Build;

// AC1 of #140: every project of the solution targets .NET 10 (LTS), and only .NET 10 (ADR-0020)
public class TargetFrameworkTests
{
    private const string Net10 = "net10.0";

    public static TheoryData<string> SolutionProjects() => [.. SolutionFiles.ProjectFiles()];

    [Fact]
    public void Solution_ListsProjects()
    {
        Assert.NotEmpty(SolutionFiles.ProjectFiles());
    }

    [Theory]
    [MemberData(nameof(SolutionProjects))]
    public void SolutionProject_TargetsOnlyNet10(string projectFile)
    {
        var frameworks = SolutionFiles.TargetFrameworks(projectFile);

        Assert.Equal(Net10, Assert.Single(frameworks));
    }
}
