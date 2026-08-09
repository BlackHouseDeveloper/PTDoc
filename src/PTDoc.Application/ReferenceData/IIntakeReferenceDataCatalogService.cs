using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

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
    IReadOnlyList<IntakeCatalogOptionDto> SearchInsuranceCarriers(string? query, int take = 10);
    IReadOnlyList<CatalogCategory> GetFunctionalLimitationCategories(BodyPart bodyPart);
    IntakeBodyPartItemDto? GetBodyPart(string bodyPartId);
    IntakeMedicationItemDto? GetMedication(string medicationId);
    IntakePainDescriptorItemDto? GetPainDescriptor(string painDescriptorId);
    IntakeCatalogOptionDto? GetComorbidity(string comorbidityId);
    IntakeCatalogOptionDto? GetAssistiveDevice(string assistiveDeviceId);
    IntakeCatalogOptionDto? GetLivingSituation(string livingSituationId);
    IntakeCatalogOptionDto? GetHouseLayoutOption(string houseLayoutOptionId);
}
