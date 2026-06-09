namespace FinancialNanoGateway.Application.Logging;

/// <summary>
/// Высокопроизводительное логирование через source generator <c>[LoggerMessage]</c>.
/// </summary>
/// <remarks>
/// Генератор разворачивает каждый partial-метод в готовый код на этапе компиляции:
/// нет боксинга аргументов, нет <c>params object[]</c>, нет парсинга шаблона в рантайме,
/// а если уровень выключен - метод выходит ДО вычисления аргументов. Это logging-аналог
/// истории про zero-allocation у метрик (TagList).
/// <para>
/// Именованные плейсхолдеры (<c>{PaymentId}</c>) сохраняют поля структурированными: в Loki
/// они станут отдельными атрибутами, по которым можно фильтровать, а не куском текста.
/// </para>
/// <para>
/// PII: логируем только безопасный контекст (Id платежа, валюту). Никаких номеров карт,
/// ФИО и прочих чувствительных данных - логи уходят в общее хранилище.
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

    // Debug: при дефолтном уровне Information эта строка НЕ пишется
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Calling bank. PaymentId={PaymentId}, Provider={Provider}")]
    public static partial void BankRequestStarting(ILogger logger, Guid paymentId, string provider);
}
