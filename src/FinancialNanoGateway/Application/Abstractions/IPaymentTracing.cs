using System.Diagnostics;
using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

public interface IPaymentTracing
{
    /// <summary>
    /// PRODUCER-span на публикацию платежа в очередь. Кладет trace-контекст (W3C traceparent)
    /// в <paramref name="headers"/>, чтобы consumer на другом потоке смог его восстановить.
    /// </summary>
    Activity? StartPublish(Payment payment, IDictionary<string, string> headers);

    /// <summary>
    /// CONSUMER-span на обработку платежа из очереди. Достает контекст producer-а из
    /// <paramref name="headers"/> и начинает <b>новый trace</b> со <b>link</b> на producer-span.
    /// </summary>
    Activity? StartProcess(Payment payment, IReadOnlyDictionary<string, string> headers);

    /// <summary>
    /// CLIENT-span на внешний вызов к банковскому провайдеру.
    /// </summary>
    Activity? StartBankRequest(Payment payment, string provider);
}
