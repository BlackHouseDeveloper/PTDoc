namespace PTDoc.Application.Pdf;

public interface IClinicalDocumentHierarchyBuilder
{
    ClinicalDocumentHierarchy Build(NoteExportDto noteData);
}
