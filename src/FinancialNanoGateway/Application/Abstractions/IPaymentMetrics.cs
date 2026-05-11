using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

/// <summary>
/// Метрики платежного домена.
/// </summary>
public interface IPaymentMetrics
{
    /// <summary>
    /// API принял валидный запрос на создание платежа.
    /// </summary>
    void PaymentRequested(Payment payment);

    /// <summary>
    /// Платеж успешно поставлен во внутреннюю очередь обработки.
    /// </summary>
    void PaymentEnqueued(Payment payment);

    /// <summary>
    /// Платеж забран из очереди или удален из нее при ошибке постановки.
    /// </summary>
    void PaymentDequeued(Payment payment);

    /// <summary>
    /// Background worker начал обработку платежа.
    /// </summary>
    void PaymentProcessingStarted(Payment payment);

    /// <summary>
    /// Background worker успешно завершил обработку платежа.
    /// </summary>
    void PaymentProcessingCompleted(Payment payment, TimeSpan duration);

    /// <summary>
    /// Background worker завершил обработку платежа с ошибкой.
    /// </summary>
    void PaymentProcessingFailed(Payment payment, string reason, TimeSpan duration);

    /// <summary>
    /// Завершился внешний вызов к банковскому провайдеру.
    /// </summary>
    void BankRequestCompleted(Payment payment, string provider, bool succeeded, TimeSpan duration);
}
