using System.Threading.Channels;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application;

public sealed class PaymentQueue : IPaymentQueue
{
    private readonly Channel<Payment> _channel = Channel.CreateUnbounded<Payment>();
    private readonly IPaymentMetrics _metrics;
    private int _count;

    public PaymentQueue(IPaymentMetrics metrics)
    {
        _metrics = metrics;
    }

    public int Count => Volatile.Read(ref _count);

    public ValueTask EnqueueAsync(Payment payment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _count);
        _metrics.PaymentEnqueued(payment);

        if (_channel.Writer.TryWrite(payment))
        {
            return ValueTask.CompletedTask;
        }

        Interlocked.Decrement(ref _count);
        _metrics.PaymentDequeued(payment);

        throw new InvalidOperationException("Payment queue is not accepting new items.");
    }

    public async IAsyncEnumerable<Payment> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var payment in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _count);
            _metrics.PaymentDequeued(payment);
            yield return payment;
        }
    }
}
