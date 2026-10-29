namespace DataTrustHub.Domain.Clearances;

// A level of sensitivity; access compares priorities only, a higher priority sees more
public sealed record ClassificationLevel(string Name, int Priority)
{
    // The levels a data item can be classified at (ADR-0011)
    public static readonly IReadOnlyList<ClassificationLevel> Standard =
    [
        new("Public", 0),
        new("Internal", 1),
        new("Confidential", 2),
        new("Secret", 3),
        new("Top Secret", 4)
    ];

    public static ClassificationLevel? FindStandard(int priority) =>
        Standard.FirstOrDefault(level => level.Priority == priority);
}
