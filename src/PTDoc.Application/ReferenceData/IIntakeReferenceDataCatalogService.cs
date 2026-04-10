namespace PTDoc.Application.ReferenceData;

public interface IIntakeReferenceDataCatalogService
{
    IntakeReferenceCatalogDto GetCatalog();
    IReadOnlyList<IntakeBodyPartGroupDto> GetBodyPartGroups();
    IReadOnlyList<IntakeMedicationItemDto> GetMedications();
    IReadOnlyList<IntakePainDescriptorItemDto> GetPainDescriptors();
    IReadOnlyList<IntakeCatalogOptionDto> GetComorbidities();
    IReadOnlyList<IntakeCatalogOptionDto> GetAssistiveDevices();
    IReadOnlyList<IntakeCatalogOptionDto> GetLivingSituations();
    IReadOnlyList<IntakeCatalogOptionDto> GetHouseLayoutOptions();
    IntakeBodyPartItemDto? GetBodyPart(string bodyPartId);
    IntakeMedicationItemDto? GetMedication(string medicationId);
    IntakePainDescriptorItemDto? GetPainDescriptor(string painDescriptorId);
    IntakeCatalogOptionDto? GetComorbidity(string comorbidityId);
    IntakeCatalogOptionDto? GetAssistiveDevice(string assistiveDeviceId);
    IntakeCatalogOptionDto? GetLivingSituation(string livingSituationId);
    IntakeCatalogOptionDto? GetHouseLayoutOption(string houseLayoutOptionId);
}
