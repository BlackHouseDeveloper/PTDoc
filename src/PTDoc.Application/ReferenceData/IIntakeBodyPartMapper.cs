using PTDoc.Core.Models;

namespace PTDoc.Application.ReferenceData;

public interface IIntakeBodyPartMapper
{
    BodyPart MapBodyPartId(string? bodyPartId);
}
