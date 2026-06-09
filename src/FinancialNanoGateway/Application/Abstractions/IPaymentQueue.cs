using FinancialNanoGateway.Application.Dtos;

namespace FinancialNanoGateway.Application.Abstractions;

public interface IPaymentQueue
{
    int Count { get; }

    ValueTask EnqueueAsync(PaymentMessageEnvelopeDto messageEnvelopeDto, CancellationToken cancellationToken);

    IAsyncEnumerable<PaymentMessageEnvelopeDto> ReadAllAsync(CancellationToken cancellationToken);
}
