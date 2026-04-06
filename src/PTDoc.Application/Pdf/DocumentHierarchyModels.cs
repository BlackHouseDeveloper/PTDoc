using PTDoc.Core.Models;

namespace PTDoc.Application.Pdf;

public enum ClinicalDocumentNodeKind
{
    Document = 0,
    Section = 1,
    Group = 2,
    Field = 3,
    Paragraph = 4,
    Table = 5,
    Todo = 6,
    Signature = 7,
    RenderHint = 8
}

public enum ClinicalDocumentSourceKind
{
    Patient = 0,
    Note = 1,
    Aggregate = 2,
    Render = 3,
    Static = 4,
    Todo = 5
}

public sealed class ClinicalDocumentHierarchy
{
    public NoteType NoteType { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public ClinicalDocumentNode Root { get; set; } = new()
    {
        Kind = ClinicalDocumentNodeKind.Document,
        Title = "Document",
        Source = ClinicalDocumentSourceKind.Static
    };
}

public sealed class ClinicalDocumentNode
{
    public string Title { get; set; } = string.Empty;
    public ClinicalDocumentNodeKind Kind { get; set; }
    public ClinicalDocumentSourceKind Source { get; set; }
    public string? Value { get; set; }
    public ClinicalDocumentTable? Table { get; set; }
    public ClinicalDocumentTodo? Todo { get; set; }
    public List<ClinicalDocumentNode> Children { get; set; } = [];
}

public sealed class ClinicalDocumentTable
{
    public List<ClinicalDocumentTableColumnGroup> ColumnGroups { get; set; } = [];
    public List<ClinicalDocumentTableColumn> Columns { get; set; } = [];
    public List<ClinicalDocumentTableRow> Rows { get; set; } = [];
}

public sealed class ClinicalDocumentTableColumnGroup
{
    public string Title { get; set; } = string.Empty;
    public int Span { get; set; }
}

public sealed class ClinicalDocumentTableColumn
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public sealed class ClinicalDocumentTableRow
{
    public List<string?> Values { get; set; } = [];
}

public sealed class ClinicalDocumentTodo
{
    public string RequiredField { get; set; } = string.Empty;
    public string SourceNeeded { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}
