namespace DataTrustHub.Features.DataManagement;

// Classification levels a data item can carry, shared by the DataManagement slices.
// A reader needs a clearance on the item's policy with a priority at least the item's.
public static class ClassificationLevels
{
    public const string PersonalMarking = "UNCLASSIFIED";

    public static readonly IReadOnlyList<ClassificationLevel> All =
    [
        new("Public", 0),
        new("Internal", 1),
        new("Confidential", 2),
        new("Secret", 3),
        new("Top Secret", 4)
    ];

    public static ClassificationLevel? FindByPriority(int priority) =>
        All.FirstOrDefault(level => level.Priority == priority);
}

public sealed record ClassificationLevel(string Name, int Priority);
