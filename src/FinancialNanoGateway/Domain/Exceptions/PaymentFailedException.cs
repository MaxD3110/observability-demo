namespace FinancialNanoGateway.Domain.Exceptions;

public sealed class PaymentFailedException : Exception
{
    public PaymentFailedException(Guid paymentId)
        : base($"Payment {paymentId} failed in the mock bank.")
    {
        PaymentId = paymentId;
    }

    public Guid PaymentId { get; }
}
