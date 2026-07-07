using PTDoc.Application.Integrations;

namespace PTDoc.UI.Services;

public interface IPaymentClientService
{
    Task<PaymentClientConfigurationResponse> GetConfigurationAsync(CancellationToken cancellationToken = default);
}
