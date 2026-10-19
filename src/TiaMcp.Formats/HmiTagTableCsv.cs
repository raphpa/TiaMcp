using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcp.Formats;

public sealed record HmiTagInfo(string Name, string DataType, string? Address, string? Connection, string? PlcName, string? PlcTag, string? Comment, string? AcquisitionCycle);

// Null optional fields mean "leave unchanged" on an existing tag.
public sealed record HmiTagSpec(string Name, string DataType, string? Address, string? Connection, string? PlcName, string? PlcTag, string? Comment = null,
    string? AcquisitionCycle = null);

/// <summary>A WinCC Unified HMI tag table as CSV, one row per tag.</summary>
public static class HmiTagTableCsv
{
    private static readonly string[] Headers =
        { "Name", "DataType", "Address", "Connection", "PlcName", "PlcTag", "Comment", "AcquisitionCycle" };

    public static string Format(IReadOnlyList<HmiTagInfo> tags) =>
        Csv.Format(Headers, tags.Select(t => new[]
            { t.Name, t.DataType, t.Address ?? "", t.Connection ?? "", t.PlcName ?? "", t.PlcTag ?? "", t.Comment ?? "", t.AcquisitionCycle ?? "" }));

    // Inverse of Format, same rules as TagTableCsv.Parse.
    public static HmiTagSpec[] Parse(string csv)
    {
        var rows = Csv.ParseRecords(csv);
        var tags = new List<HmiTagSpec>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var line = i + 2; // 1-based, after the header row
            var name = Csv.Get(r, "Name");
            var dataType = Csv.Get(r, "DataType");
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(dataType)) continue; // blank line
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dataType))
                throw new FormatException($"CSV line {line}: Name and DataType are required.");

            tags.Add(new HmiTagSpec(name!, dataType!,
                Csv.GetOptional(r, "Address"), Csv.GetOptional(r, "Connection"),
                Csv.GetOptional(r, "PlcName"), Csv.GetOptional(r, "PlcTag"), Csv.GetOptional(r, "Comment"),
                Csv.GetOptional(r, "AcquisitionCycle")));
        }
        return tags.ToArray();
    }
}
