using TiaMcp.Openness;

namespace TiaMcp.Mcp;

// One parsed 'OK'/'FAILED'/'STALE' line from read_source_tree's _export_summary.txt (or a
// hand-trimmed copy/inline excerpt of it) - see write_source_tree. A plain class rather than a
// record: this project (net48) has no IsExternalInit polyfill for record/init-only support.
internal sealed class ImportListItem
{
    public ImportListItem(string deviceName, string kind, string groupPath, string itemName, bool ok)
    {
        DeviceName = deviceName;
        Kind = kind;
        GroupPath = groupPath;
        ItemName = itemName;
        Ok = ok;
    }

    public string DeviceName { get; }
    public string Kind { get; }
    public string GroupPath { get; }
    public string ItemName { get; }
    public bool Ok { get; }

    public bool Is(string device, string kind, string name) =>
        string.Equals(DeviceName, device, StringComparison.OrdinalIgnoreCase)
        && Kind == kind
        && string.Equals(ItemName, name, StringComparison.OrdinalIgnoreCase);
}

// One item as exported from TIA Portal: its files (root-relative path, content, whether it is
// written with a UTF-8 BOM), or why it couldn't be exported. An item of a kind with a fixed
// file (the HMI alarm files) has no name.
internal sealed class ExportedItem
{
    public ExportedItem(string device, string kind, string groupPath, string name, List<(string Path, string Content, bool Bom)> files, string? error)
    {
        Device = device; Kind = kind; GroupPath = groupPath; Name = name; Files = files; Error = error;
    }
    public string Device { get; }
    public string Kind { get; }
    public string GroupPath { get; }
    public string Name { get; }
    public List<(string Path, string Content, bool Bom)> Files { get; }
    public string? Error { get; }
    public bool Ok => Error == null;

    // Blocks and UDTs: the documents TIA Portal exports (.s7dcl/.s7res, .graph.il, .awl, companions).
    public static ExportedItem FromDocuments(string device, string kind, string groupPath, string name, ExportResult result)
    {
        var dir = SourceTree.ItemDir(device, kind, groupPath);
        var files = result.Documents.Select(d => ($"{dir}/{SourceTree.Sanitize(SourceTree.EnsureExtension(d.FileName))}", d.Content, false)).ToList();
        return new ExportedItem(device, kind, groupPath, name, files, result.Success ? null : result.Error ?? "export failed");
    }

    // CSV gets a UTF-8 BOM so Excel auto-detects the encoding and opening the file directly
    // (double-click) renders non-ASCII comment/tag text correctly instead of mangling it.
    public static ExportedItem FromCsv(string device, string kind, string groupPath, string name, string? csv, string? error)
    {
        var files = csv == null
            ? new List<(string, string, bool)>()
            : new List<(string, string, bool)> { ($"{SourceTree.ItemDir(device, kind, groupPath)}/{SourceTree.Sanitize(SourceTree.ItemStem(kind, name))}.csv", csv, true) };
        return new ExportedItem(device, kind, groupPath, name, files, csv == null ? error ?? "export failed" : null);
    }
}
