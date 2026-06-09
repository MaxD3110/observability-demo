using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

/// <summary>
/// Metrics for the payment domain.
/// </summary>
public interface IPaymentMetrics
{
    /// <summary>
    /// The API accepted a valid request to create a payment.
    /// </summary>
    void PaymentRequested(Payment payment);

    /// <summary>
    /// The payment was successfully placed on the internal processing queue.
    /// </summary>
    void PaymentEnqueued(Payment payment);

    /// <summary>
    /// The payment was taken from the queue, or removed from it after a failed enqueue.
    /// </summary>
    void PaymentDequeued(Payment payment);

    /// <summary>
    /// The background worker started processing the payment.
    /// </summary>
    void PaymentProcessingStarted(Payment payment);

    /// <summary>
    /// The background worker finished processing the payment successfully.
    /// </summary>
    void PaymentProcessingCompleted(Payment payment, TimeSpan duration);

    /// <summary>
    /// The background worker finished processing the payment with an error.
    /// </summary>
    void PaymentProcessingFailed(Payment payment, string reason, TimeSpan duration);

    /// <summary>
    /// An external call to the bank provider finished.
    /// </summary>
    void BankRequestCompleted(Payment payment, string provider, bool succeeded, TimeSpan duration);
}
