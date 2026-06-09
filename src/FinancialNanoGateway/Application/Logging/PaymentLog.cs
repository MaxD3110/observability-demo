namespace FinancialNanoGateway.Application.Logging;

/// <summary>
/// High-performance logging via the <c>[LoggerMessage]</c> source generator.
/// </summary>
/// <remarks>
/// The generator expands each partial method into ready-made code at compile time:
/// no argument boxing, no <c>params object[]</c>, no message-template parsing at runtime,
/// and if the level is disabled the method returns BEFORE evaluating its arguments. This is the
/// logging counterpart of the zero-allocation story from the metrics (TagList).
/// <para>
/// Named placeholders (<c>{PaymentId}</c>) keep fields structured: in Loki they become
/// separate attributes you can filter on, instead of an opaque chunk of text.
/// </para>
/// <para>
/// PII: we log only safe context (payment id, currency). No card numbers, names, or other
/// sensitive data - logs go to shared storage.
/// </para>
/// </remarks>
internal static partial class PaymentLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Payment accepted. PaymentId={PaymentId}, Currency={Currency}")]
    public static partial void PaymentAccepted(ILogger logger, Guid paymentId, string currency);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Payment processed. PaymentId={PaymentId}, DurationMs={DurationMs}")]
    public static partial void PaymentProcessed(ILogger logger, Guid paymentId, double durationMs);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Payment failed. PaymentId={PaymentId}, Reason={Reason}")]
    public static partial void PaymentProcessingFailed(ILogger logger, Exception exception, Guid paymentId, string reason);

    // Debug: at the default Information level this line is NOT written.
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Calling bank. PaymentId={PaymentId}, Provider={Provider}")]
    public static partial void BankRequestStarting(ILogger logger, Guid paymentId, string provider);
}
