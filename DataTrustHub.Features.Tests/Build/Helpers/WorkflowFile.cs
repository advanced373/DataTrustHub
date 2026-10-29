using System.Text.RegularExpressions;

namespace DataTrustHub.Features.Tests.Build.Helpers;

/// <summary>A step of a workflow job: its name and its full text (uses, with, env and run lines).</summary>
internal sealed record WorkflowStep(string Name, string Text);

/// <summary>
/// A GitHub Actions workflow read as text, without a YAML library (the test project has none): blocks are
/// cut by indentation, which is enough for the two-space layout the repository's workflows use.
/// </summary>
internal sealed class WorkflowFile
{
    private const string JobsKey = "jobs";
    private const string StepsKey = "steps:";
    private const int JobIndent = 2;

    private static readonly Regex StepName = new(@"^\s*-?\s*name:\s*(?<name>.+?)\s*$", RegexOptions.Multiline);

    private readonly IReadOnlyList<string> lines;

    internal WorkflowFile(string text)
    {
        Text = text.Replace("\r\n", "\n");
        lines = Text.Split('\n');
    }

    internal string Text { get; }

    /// <summary>The comment lines at the top of the file, before the first key.</summary>
    internal string HeaderComment() =>
        string.Join('\n', lines.TakeWhile(line => line.StartsWith('#')));

    /// <summary>A top-level key and the lines indented under it.</summary>
    internal string Section(string key) => string.Join('\n', Block(0, Indexed(0).First(item => IsKey(item.Line, key)).Index));

    /// <summary>The names of the jobs, in the order they are written.</summary>
    internal IReadOnlyList<string> JobNames() =>
        Indexed(JobIndent).Where(item => item.Index > JobsIndex())
            .Select(item => item.Line.Trim().TrimEnd(':'))
            .ToList();

    /// <summary>A job's key and the lines indented under it.</summary>
    internal string Job(string name)
    {
        var start = Indexed(JobIndent).First(item => item.Index > JobsIndex() && IsKey(item.Line, name)).Index;
        return string.Join('\n', Block(JobIndent, start));
    }

    /// <summary>The job's text without its steps: runs-on, environment, permissions, env, needs, if.</summary>
    internal string JobHeader(string name) =>
        string.Join('\n', Job(name).Split('\n').TakeWhile(line => line.Trim() != StepsKey));

    /// <summary>The job's steps, in order.</summary>
    internal IReadOnlyList<WorkflowStep> Steps(string jobName)
    {
        var jobLines = Job(jobName).Split('\n');
        var stepLines = jobLines.SkipWhile(line => line.Trim() != StepsKey).Skip(1).ToList();
        var stepIndent = Indent(stepLines.First(line => line.TrimStart().StartsWith("- ")));
        var steps = new List<List<string>>();
        foreach (var line in stepLines)
        {
            if (Indent(line) == stepIndent && line.TrimStart().StartsWith("- "))
                steps.Add([]);
            if (steps.Count > 0)
                steps[^1].Add(line);
        }

        return steps.Select(ToStep).ToList();
    }

    private static WorkflowStep ToStep(List<string> stepLines)
    {
        var text = string.Join('\n', stepLines);
        var name = StepName.Match(text);
        return new WorkflowStep(name.Success ? name.Groups["name"].Value : string.Empty, text);
    }

    private int JobsIndex() => Indexed(0).First(item => IsKey(item.Line, JobsKey)).Index;

    // The lines with exactly this indent that open a key, with their index
    private IEnumerable<(string Line, int Index)> Indexed(int indent) =>
        lines.Select((line, index) => (Line: line, Index: index))
            .Where(item => IsContent(item.Line) && Indent(item.Line) == indent && !item.Line.TrimStart().StartsWith('-'));

    // From the key line at start, every following line that is blank, a comment or indented deeper
    private IEnumerable<string> Block(int indent, int start) =>
        lines.Skip(start).Take(1)
            .Concat(lines.Skip(start + 1).TakeWhile(line => !IsContent(line) || Indent(line) > indent));

    private static bool IsKey(string line, string key) => line.Trim().StartsWith(key + ":");

    private static bool IsContent(string line) => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#');

    private static int Indent(string line) => line.Length - line.TrimStart().Length;
}
