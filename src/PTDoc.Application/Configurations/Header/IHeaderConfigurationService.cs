namespace PTDoc.Application.Configurations.Header;

public interface IHeaderConfigurationService
{
    HeaderConfiguration GetConfiguration(string route);
}