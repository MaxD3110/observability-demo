using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Abstractions;

public interface IPaymentMetrics
{
    void PaymentRequested(Payment payment);

    void PaymentEnqueued(Payment payment);

    void PaymentDequeued(Payment payment);

    void PaymentProcessingStarted(Payment payment);

    void PaymentProcessingCompleted(Payment payment, TimeSpan duration);

    void PaymentProcessingFailed(Payment payment, string reason, TimeSpan duration);

    void BankRequestCompleted(Payment payment, string provider, bool succeeded, TimeSpan duration);
}
