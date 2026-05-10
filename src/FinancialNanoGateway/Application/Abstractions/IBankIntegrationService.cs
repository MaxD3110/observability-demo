using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

public interface IBankIntegrationService
{
    Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken);
}
