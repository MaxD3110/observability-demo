using System.Threading.Channels;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;

namespace FinancialNanoGateway.Application;

public sealed class PaymentQueue : IPaymentQueue
{
    private readonly Channel<PaymentMessageEnvelopeDto> _channel;
    private readonly IPaymentMetrics _metrics;

    public PaymentQueue(Channel<PaymentMessageEnvelopeDto> channel, IPaymentMetrics metrics)
    {
        _channel = channel;
        _metrics = metrics;
    }

    // The channel already tracks its own length, so we read it directly instead of
    // hand-maintaining a parallel counter. This is the same source the observable gauge reads.
    public int Count => _channel.Reader.Count;

    public ValueTask EnqueueAsync(PaymentMessageEnvelopeDto messageEnvelopeDto, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _metrics.PaymentEnqueued(messageEnvelopeDto.Payment);

        if (_channel.Writer.TryWrite(messageEnvelopeDto))
        {
            return ValueTask.CompletedTask;
        }

        _metrics.PaymentDequeued(messageEnvelopeDto.Payment);

        throw new InvalidOperationException("Payment queue is not accepting new items.");
    }

    public async IAsyncEnumerable<PaymentMessageEnvelopeDto> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            _metrics.PaymentDequeued(message.Payment);
            yield return message;
        }
    }
}
