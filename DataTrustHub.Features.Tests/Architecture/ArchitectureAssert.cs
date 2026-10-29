using NetArchTest.Rules;
using Xunit;

namespace DataTrustHub.Features.Tests.Architecture;

internal static class ArchitectureAssert
{
    internal static void Passed(TestResult result, string rule) =>
        Passed(result.FailingTypeNames ?? [], rule);

    internal static void Passed(IReadOnlyCollection<string> failingTypeNames, string rule) =>
        Assert.True(
            failingTypeNames.Count == 0,
            $"{rule}. Violations:\n{string.Join("\n", failingTypeNames)}");

    // Guards against a rule passing only because its type filter matched nothing
    internal static void NotEmpty(PredicateList selection, string description) =>
        Assert.True(selection.GetTypes().Any(), $"No types matched: {description}");
}
