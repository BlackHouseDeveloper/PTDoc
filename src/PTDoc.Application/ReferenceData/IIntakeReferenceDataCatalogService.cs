namespace PTDoc.Application.ReferenceData;

public interface IIntakeReferenceDataCatalogService
{
    IntakeReferenceCatalogDto GetCatalog();
    IReadOnlyList<IntakeBodyPartGroupDto> GetBodyPartGroups();
    IReadOnlyList<IntakeMedicationItemDto> GetMedications();
    IReadOnlyList<IntakePainDescriptorItemDto> GetPainDescriptors();
    IntakeBodyPartItemDto? GetBodyPart(string bodyPartId);
    IntakeMedicationItemDto? GetMedication(string medicationId);
    IntakePainDescriptorItemDto? GetPainDescriptor(string painDescriptorId);
}
