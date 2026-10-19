using TiaMcp.Openness;

namespace TiaMcp.Mcp;

/// <summary>Paths and small helpers of the source tree, shared by the tools and the kind handlers.</summary>
internal static class SourceTree
{
    public static string SectionOf(string kind) =>
        SourceKinds.Find(kind)?.Section ?? throw new ArgumentException($"Unknown kind '{kind}'.", nameof(kind));

    // The file name (without extension) an item's files start with: its name, or the fixed file
    // name of a kind such as the HMI alarm files.
    public static string ItemStem(string kind, string name) => SourceKinds.Find(kind)?.FixedName ?? name;

    // How an item appears in _export_summary.txt, list_project and tool results - the format
    // SourceKinds.ListedItem reads back.
    public static string Describe(string kind, string groupPath, string name) =>
        SourceKinds.IsFixed(kind) ? kind : $"{kind} '{Label(groupPath, name)}'";

    // Root-relative, '/'-separated folder of an item: <device>/<section>/<group path>.
    public static string ItemDir(string device, string kind, string groupPath)
    {
        var segments = new List<string> { Sanitize(device), SectionOf(kind) };
        if (!string.IsNullOrEmpty(groupPath))
            segments.AddRange(groupPath.Split('/').Select(Sanitize));
        return string.Join("/", segments);
    }

    public static string GroupDir(string deviceDir, string sectionName, string groupPath)
    {
        var segments = new List<string> { deviceDir, sectionName };
        if (!string.IsNullOrEmpty(groupPath))
            segments.AddRange(groupPath.Split('/').Select(Sanitize));
        return Path.Combine(segments.ToArray());
    }

    public static string Label(string groupPath, string name) =>
        string.IsNullOrEmpty(groupPath) ? name : $"{groupPath}/{name}";

    public static string EnsureExtension(string fileName) =>
        string.IsNullOrEmpty(Path.GetExtension(fileName)) ? fileName + ".txt" : fileName;

    public static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    // Groups with no table anywhere below them - otherwise empty groups would be invisible.
    public static IEnumerable<string> EmptyGroups(IEnumerable<string> tableGroupPaths, IReadOnlyList<string> groups)
    {
        var used = tableGroupPaths.ToList();
        return groups.Where(g => !used.Any(p => p == g || p.StartsWith(g + "/")));
    }

    // Best-effort recursive group creation so the create calls' "group must already
    // exist" requirement doesn't block restoring a full
    // tree into an empty/new PLC or HMI. Failures (including "already exists") are ignored here - a
    // genuine problem still surfaces from the subsequent create call itself right after this runs.
    // ensured: group paths already handled in this import, so each folder is created (or found to
    // exist - a create that throws, which is slow over Openness) once rather than once per item.
    public static async Task EnsureGroupPath(string groupPath, Func<string, string, Task> createGroup, ISet<string> ensured)
    {
        if (string.IsNullOrEmpty(groupPath)) return;
        var current = "";
        foreach (var segment in groupPath.Split('/'))
        {
            var path = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
            if (ensured.Add(path)) await createGroup(current, segment);
            current = path;
        }
    }

    public static string DeletedTags(WriteTagsResult result)
    {
        var deleted = result.Messages.Where(m => m.StartsWith("Deleted '")).Select(m => m.Substring("Deleted ".Length).TrimEnd('.')).ToList();
        return deleted.Count == 0 ? "" : $", deleted {string.Join(", ", deleted)}";
    }
}
