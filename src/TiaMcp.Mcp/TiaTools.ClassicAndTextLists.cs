using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TiaMcp.Openness;

namespace TiaMcp.Mcp;

/// <summary>
/// Source tree support for classic WinCC HMIs (Comfort/Advanced/RT Advanced) and PLC alarm text
/// lists. Classic objects have no typed model in Openness, so they live in the tree as their native
/// Openness XML; alarm text lists as one CSV per list.
/// </summary>
public sealed partial class TiaTools
{
    // Tree kind <-> classic kind (HmiClassicKinds). Classic tag tables share the kind (and folder)
    // with WinCC Unified tag tables; which one applies follows from the HMI.
    private static readonly (string TreeKind, string ClassicKind)[] ClassicKinds =
    {
        ("HMI text list", HmiClassicKinds.TextList),
        ("HMI tag table", HmiClassicKinds.TagTable),
        ("HMI screen template", HmiClassicKinds.ScreenTemplate),
        ("HMI pop-up screen", HmiClassicKinds.PopupScreen),
        ("HMI screen", HmiClassicKinds.Screen),
    };

    private const string AlarmTextListKind = "alarm text list";

    private static string TreeKindOf(string classicKind) => ClassicKinds.First(k => k.ClassicKind == classicKind).TreeKind;

    private static string ClassicKindOf(string treeKind) => ClassicKinds.First(k => k.TreeKind == treeKind).ClassicKind;

    private static bool IsClassicTreeKind(string kind) => ClassicKinds.Any(k => k.TreeKind == kind);

    // Notes of the last ExportItems for the read result (e.g. graphic lists that are skipped).
    private readonly List<string> _exportNotes = new();

    private static ExportedItem FromXml(string device, string kind, string groupPath, string name, string? xml, string? error)
    {
        var files = xml == null
            ? new List<(string, string, bool)>()
            : new List<(string, string, bool)> { ($"{ItemDir(device, kind, groupPath)}/{Sanitize(name)}.xml", xml, false) };
        return new ExportedItem(device, kind, groupPath, name, files, xml == null ? error ?? "export failed" : null);
    }

    private async Task ExportPlcAlarmTextLists(string plc, Func<string, string, string, string, bool> includes, List<ExportedItem> items)
    {
        var names = (await _session.ListPlcAlarmTextListNamesAsync(plc)).Where(n => includes(plc, AlarmTextListKind, "", n)).ToList();
        if (names.Count == 0) return;
        var read = await _session.ReadPlcAlarmTextListsAsync(plc);
        foreach (var name in names)
        {
            var list = read.Lists.FirstOrDefault(l => l.Name == name);
            items.Add(FromCsv(plc, AlarmTextListKind, "", name,
                list == null ? null : AlarmTextListCsv.Format(list),
                read.Error ?? (list == null ? "not in the text list export" : null)));
        }
    }

    private async Task ExportHmiClassic(string hmi, Func<string, string, string, string, bool> includes, bool all, List<ExportedItem> items)
    {
        foreach (var o in await _session.ReadHmiClassicObjectsAsync(hmi, (kind, folder, name) => includes(hmi, TreeKindOf(kind), folder, name)))
            items.Add(FromXml(hmi, TreeKindOf(o.Kind), o.FolderPath, o.Name, o.Xml, o.Error));

        if (all)
        {
            var graphicLists = await _session.CountHmiClassicGraphicListsAsync(hmi);
            if (graphicLists > 0)
                _exportNotes.Add($"SKIPPED {graphicLists} graphic list(s) of '{hmi}' - exporting a classic graphic list crashes TIA Portal V21 (InvalidCastException in UpdateOpenLinks)");
        }
    }

    private async Task<(List<string> Created, List<string> Updated, List<string> Skipped, List<string> Failed)> ImportAlarmTextLists(
        string plcName, string plcDir, List<ImportListItem> items, bool createMissing, bool overwriteExisting, bool dryRun, bool deleteMissing, List<ImportListItem> written)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        var existing = new HashSet<string>(await _session.ListPlcAlarmTextListNamesAsync(plcName), StringComparer.Ordinal);

        var lists = new List<(ImportListItem Item, PlcAlarmTextList List)>();
        foreach (var item in items)
        {
            var label = $"alarm text list '{item.ItemName}'";
            var file = Path.Combine(plcDir, "PLC alarm text lists", Sanitize(item.ItemName) + ".csv");
            if (!File.Exists(file)) { failed.Add($"{label}: no .csv file found at {file}"); continue; }
            if (!existing.Contains(item.ItemName))
            {
                if (createMissing) failed.Add($"{label}: the list doesn't exist in PLC '{plcName}' - text lists can't be created through Openness, create it in TIA Portal first");
                else skipped.Add($"{label} - does not exist, createMissing=false");
                continue;
            }
            if (!overwriteExisting) { skipped.Add($"{label} - already exists, overwriteExisting=false"); continue; }
            try { lists.Add((item, AlarmTextListCsv.Parse(item.ItemName, File.ReadAllText(file)))); }
            catch (FormatException ex) { failed.Add($"{label}: {ex.Message}"); }
        }
        if (lists.Count == 0) return (created, updated, skipped, failed);

        // All lists of the PLC in one import: TIA Portal replaces whole lists, so one fresh export
        // is patched with every list being written.
        var result = await _session.WritePlcAlarmTextListsAsync(plcName, lists.Select(l => l.List).ToList(), deleteMissing, dryRun);
        foreach (var (item, list) in lists)
        {
            var change = result.Changes.FirstOrDefault(c => c.List == list.Name);
            var label = $"alarm text list '{list.Name}'";
            if (!result.Success) { failed.Add($"{label}: {result.Message}"); continue; }
            var counts = change == null ? "" : $"{change.Added.Count} new, {change.Changed.Count} changed, {change.Deleted.Count} deleted";
            var detail = change == null ? "" : string.Concat(change.Added.Take(5).Select(a => $"\n      + {a}"))
                + string.Concat(change.Changed.Take(5).Select(c => $"\n      ~ {c}"))
                + string.Concat(change.Deleted.Take(5).Select(d => $"\n      - {d}"));
            updated.Add($"{label} ({(dryRun ? "dry run, " : "")}{counts}){detail}");
            if (!dryRun) written.Add(item);
        }
        if (result.Success && result.Message != null) updated.Add($"alarm text lists: {result.Message}");
        return (created, updated, skipped, failed);
    }

    private async Task<(List<string> Created, List<string> Updated, List<string> Skipped, List<string> Failed)> ImportHmiClassic(
        string hmiName, string hmiDir, List<ImportListItem> items, bool createMissing, bool overwriteExisting, bool dryRun, List<ImportListItem> written)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        var existing = new HashSet<string>((await _session.ListHmiClassicObjectsAsync(hmiName))
            .Where(o => o.Kind != HmiClassicKinds.GraphicList)
            .Select(o => $"{TreeKindOf(o.Kind)}|{o.Name}"), StringComparer.Ordinal);

        // Dependency order: text lists, tag tables, templates and pop-ups, then screens.
        foreach (var (treeKind, classicKind) in ClassicKinds)
        {
            foreach (var item in items.Where(i => i.Kind == treeKind))
            {
                var label = Describe(item.Kind, item.GroupPath, item.ItemName);
                var file = Path.Combine(hmiDir, ItemDir("", item.Kind, item.GroupPath).TrimStart('/'), Sanitize(item.ItemName) + ".xml");
                if (!File.Exists(file)) { failed.Add($"{label}: no .xml file found at {file}"); continue; }

                var exists = existing.Contains($"{treeKind}|{item.ItemName}");
                if (exists && !overwriteExisting) { skipped.Add($"{label} - already exists, overwriteExisting=false"); continue; }
                if (!exists && !createMissing) { skipped.Add($"{label} - does not exist, createMissing=false"); continue; }
                if (dryRun) { (exists ? updated : created).Add($"{label} (dry run)"); continue; }

                var result = await _session.WriteHmiClassicObjectAsync(hmiName, classicKind, item.GroupPath, item.ItemName, File.ReadAllText(file), create: !exists);
                if (!result.Success) { failed.Add($"{label}: {result.Error}"); continue; }
                (exists ? updated : created).Add(label);
                existing.Add($"{treeKind}|{item.ItemName}");
                written.Add(item);
            }
        }
        return (created, updated, skipped, failed);
    }
}
