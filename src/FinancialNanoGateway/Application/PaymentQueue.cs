using System.Threading.Channels;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;

namespace FinancialNanoGateway.Application;

public sealed class PaymentQueue : IPaymentQueue
{
    private readonly Channel<PaymentMessageEnvelopeDto> _channel = Channel.CreateUnbounded<PaymentMessageEnvelopeDto>();
    private readonly IPaymentMetrics _metrics;
    private int _count;

    public PaymentQueue(IPaymentMetrics metrics)
    {
        _metrics = metrics;
    }

    public int Count => Volatile.Read(ref _count);

    public ValueTask EnqueueAsync(PaymentMessageEnvelopeDto messageEnvelopeDto, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _count);
        _metrics.PaymentEnqueued(messageEnvelopeDto.Payment);

        if (_channel.Writer.TryWrite(messageEnvelopeDto))
        {
            return ValueTask.CompletedTask;
        }

        Interlocked.Decrement(ref _count);
        _metrics.PaymentDequeued(messageEnvelopeDto.Payment);

        throw new InvalidOperationException("Payment queue is not accepting new items.");
    }

    public async IAsyncEnumerable<PaymentMessageEnvelopeDto> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _count);
            _metrics.PaymentDequeued(message.Payment);
            yield return message;
        }
    }
}
