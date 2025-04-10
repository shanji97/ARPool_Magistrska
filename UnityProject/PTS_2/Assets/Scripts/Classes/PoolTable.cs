using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class PoolTable
{
    public string Name { get; private set; }
    public float Length { get; private set; }
    public float Width { get; private set; }
    public bool StandardTable { get; private set; }
    public TableType Type { get; private set; }

    public static readonly float SizeTolerance = 0.03f;

    // Static definitions of standard tables
    public static readonly List<PoolTable> StandardTables = new()
    {
        new PoolTable("Small (7ft)", 2.13f, 1.07f, TableType.Small),
        new PoolTable("Medium (8ft)", 2.44f, 1.22f, TableType.Medium),
        new PoolTable("Large (9ft)", 2.84f, 1.42f, TableType.Large)
    };

    private PoolTable(string name, float length, float width, TableType type)
    {
        Name = name;
        Length = length;
        Width = width;
        Type = type;
        StandardTable = true;
    }

    public PoolTable(TableType typeOfStandardTable)
    {
        var preset = StandardTables.FirstOrDefault(t => t.Type == typeOfStandardTable);
        if (preset != null)
        {
            Name = preset.Name;
            Length = preset.Length;
            Width = preset.Width;
            Type = preset.Type;
            StandardTable = true;
        }
        else
        {
            Name = "Unknown";
            Type = TableType.Custom;
            StandardTable = false;
        }
    }

    public PoolTable(float length, float width, string name = "")
    {
        if(width > length)
            (length, width) = (width, length);

        TrySetStandardTable(length, width);
        if (!StandardTable)
            AssignCustomTable(length, width, name);
    }

    private void AssignCustomTable(float length, float width, string name = "")
    {
        Length = length;
        Width = width;
        Name = string.IsNullOrWhiteSpace(name) ? "Custom Table" + DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss") : name;
        Type = TableType.Custom;
        StandardTable = false;
    }

    private void TrySetStandardTable(float length, float width)
    {
        var match = StandardTables.FirstOrDefault(t =>
            Mathf.Abs(t.Length - length) <= SizeTolerance &&
            Mathf.Abs(t.Width - width) <= SizeTolerance);

        if (match != null)
        {
            Length = match.Length;
            Width = match.Width;
            Name = match.Name + " (Auto)";
            Type = match.Type;
            StandardTable = true;
        }
    }

    public bool IsStandardTable(float length, float width) =>
        StandardTables.Any(t =>
            Mathf.Abs(t.Length - length) <= SizeTolerance &&
            Mathf.Abs(t.Width - width) <= SizeTolerance);

    public (float Length, float Width, string Name) GetTableData()
    {
        return (Length, Width, Name);
    }

    public override string ToString()
    {
        return $"{Name} — {Length:F2}m x {Width:F2}m [{Type}]";
    }
}