using System.Diagnostics;
using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

public interface IPaymentTracing
{
    /// <summary>
    /// PRODUCER span for publishing a payment to the queue. Puts the trace context (W3C traceparent)
    /// into <paramref name="headers"/> so the consumer on another thread can restore it.
    /// </summary>
    Activity? StartPublish(Payment payment, IDictionary<string, string> headers);

    /// <summary>
    /// CONSUMER span for processing a payment from the queue. Extracts the producer context from
    /// <paramref name="headers"/> and starts a <b>new trace</b> with a <b>link</b> to the producer span.
    /// </summary>
    Activity? StartProcess(Payment payment, IReadOnlyDictionary<string, string> headers);

    /// <summary>
    /// CLIENT span for the external call to the bank provider.
    /// </summary>
    Activity? StartBankRequest(Payment payment, string provider);
}
