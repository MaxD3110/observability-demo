using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

public interface IPaymentQueue
{
    int Count { get; }

    ValueTask EnqueueAsync(Payment payment, CancellationToken cancellationToken);

    IAsyncEnumerable<Payment> ReadAllAsync(CancellationToken cancellationToken);
}
