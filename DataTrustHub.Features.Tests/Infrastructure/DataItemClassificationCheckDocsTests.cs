using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #128 AC5: the D1 row of docs/domain/aggregates.md names the check constraint as an enforcement point, and
// docs/architecture/data-model.md mentions it on the DataItem table.
public class DataItemClassificationCheckDocsTests
{
    private const string AggregatesDoc = "docs/domain/aggregates.md";
    private const string DataModelDoc = "docs/architecture/data-model.md";
    private const string ConstraintName = DataItemClassificationCheckModelTests.ConstraintName;
    private const string D1RowStart = "| D1 |";
    private const string DataItemHeading = "### DataItem";
    private const string NextHeading = "### ";

    // The enforcement column (the last cell) holds it, next to the factories and the use case
    [Fact]
    public void Aggregates_D1Row_EnforcedInColumn_HasTheCheckConstraint()
    {
        var row = Assert.Single(Lines(AggregatesDoc), line => line.StartsWith(D1RowStart, StringComparison.Ordinal));
        var enforcedIn = row.Split('|', StringSplitOptions.RemoveEmptyEntries)[^1];

        Assert.Contains(ConstraintName, enforcedIn);
    }

    [Fact]
    public void DataModel_DataItemTable_MentionsTheCheckConstraint()
    {
        var text = SolutionFiles.ReadText(DataModelDoc);
        var start = text.IndexOf(DataItemHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, DataItemHeading);
        var end = text.IndexOf(NextHeading, start + DataItemHeading.Length, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        Assert.Contains(ConstraintName, section);
    }

    private static string[] Lines(string path) => SolutionFiles.ReadText(path).Split('\n').Select(line => line.TrimEnd()).ToArray();
}
