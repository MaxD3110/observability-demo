using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Infrastructure.Observability;

public sealed class PaymentMetrics : IPaymentMetrics
{
    public const string MeterName = "FinancialNanoGateway.Payments";

    private const string CurrencyTag = "currency";
    private const string ProviderTag = "provider";
    private const string OutcomeTag = "outcome";
    private const string ReasonTag = "reason";
    private const string SuccessOutcome = "success";
    private const string FailedOutcome = "failed";

    private readonly Counter<long> _paymentRequests;
    private readonly Counter<long> _paymentEnqueued;
    private readonly Counter<long> _paymentProcessed;
    private readonly Counter<long> _paymentFailed;
    private readonly Counter<double> _paymentValue;
    private readonly Counter<double> _bankRequestValue;
    private readonly Histogram<double> _paymentAmount;
    private readonly Histogram<double> _paymentQueueWaitDuration;
    private readonly Histogram<double> _paymentProcessingDuration;
    private readonly Histogram<double> _bankRequestDuration;

    // Observable instruments должны жить столько же, сколько Meter. Поэтому храним ссылки в полях,
    // даже если дальше напрямую к ним не обращаемся.
    private readonly ObservableGauge<int> _queueDepth;
    private readonly ObservableGauge<int> _activeProcessing;

    private int _currentQueueDepth;
    private int _currentActiveProcessing;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        // Counter подходит для событий, которые только увеличиваются.
        _paymentRequests = meter.CreateCounter<long>(
            "payment_requests",
            unit: "{payment}",
            description: "Количество валидных платежных запросов, принятых API.");

        _paymentEnqueued = meter.CreateCounter<long>(
            "payment_enqueued",
            unit: "{payment}",
            description: "Количество платежей, успешно поставленных в очередь.");

        _paymentProcessed = meter.CreateCounter<long>(
            "payment_processed",
            unit: "{payment}",
            description: "Количество платежей, обработанных background worker.");

        _paymentFailed = meter.CreateCounter<long>(
            "payment_failed",
            unit: "{payment}",
            description: "Количество платежей, завершившихся ошибкой.");

        _paymentValue = meter.CreateCounter<double>(
            "payment_value",
            description: "Суммарная стоимость платежей по результату обработки. Валюта и outcome передаются labels.");

        _bankRequestValue = meter.CreateCounter<double>(
            "bank_request_value",
            description: "Суммарная стоимость платежей по банковскому провайдеру и результату вызова.");

        // Histogram нужен для распределений: по нему считаются p50/p95/p99, а не только среднее.
        _paymentAmount = meter.CreateHistogram<double>(
            "payment_amount",
            description: "Распределение сумм входящих платежей. Валюта передается label-ом.");

        _paymentQueueWaitDuration = meter.CreateHistogram<double>(
            "payment_queue_wait_duration",
            unit: "ms",
            description: "Сколько времени платеж провел в очереди до начала обработки.");

        _paymentProcessingDuration = meter.CreateHistogram<double>(
            "payment_processing_duration",
            unit: "ms",
            description: "Полная длительность обработки платежа background worker-ом.");

        _bankRequestDuration = meter.CreateHistogram<double>(
            "bank_request_duration",
            unit: "ms",
            description: "Длительность вызова к банковскому провайдеру.");

        // Gauge показывает текущее состояние системы. В отличие от Counter, значение может расти и падать.
        // В данном случае используется observable вариант, т.к. метрика показывает состояние системы (pull), а не событие (push).
        _queueDepth = meter.CreateObservableGauge(
            "payment_queue_depth",
            () => Math.Max(0, Volatile.Read(ref _currentQueueDepth)),
            unit: "{payment}",
            description: "Текущее количество платежей, ожидающих обработки.");

        _activeProcessing = meter.CreateObservableGauge(
            "payment_active_processing",
            () => Math.Max(0, Volatile.Read(ref _currentActiveProcessing)),
            unit: "{payment}",
            description: "Текущее количество платежей, которые обрабатываются прямо сейчас.");
    }

    public void PaymentRequested(Payment payment)
    {
        var tags = CreatePaymentTags(payment);

        _paymentRequests.Add(1, tags);
        _paymentAmount.Record((double)payment.Amount, tags);
    }

    public void PaymentEnqueued(Payment payment)
    {
        Interlocked.Increment(ref _currentQueueDepth);
        _paymentEnqueued.Add(1, CreatePaymentTags(payment));
    }

    public void PaymentDequeued(Payment payment)
    {
        Interlocked.Decrement(ref _currentQueueDepth);

        var queueWaitDuration = DateTime.UtcNow - payment.CreatedAt;
        if (queueWaitDuration >= TimeSpan.Zero)
        {
            _paymentQueueWaitDuration.Record(queueWaitDuration.TotalMilliseconds, CreatePaymentTags(payment));
        }
    }

    public void PaymentProcessingStarted(Payment payment)
    {
        Interlocked.Increment(ref _currentActiveProcessing);
    }

    public void PaymentProcessingCompleted(Payment payment, TimeSpan duration)
    {
        Interlocked.Decrement(ref _currentActiveProcessing);

        var tags = CreatePaymentTags(payment, outcome: SuccessOutcome);
        _paymentProcessed.Add(1, tags);
        _paymentValue.Add((double)payment.Amount, tags);
        _paymentProcessingDuration.Record(duration.TotalMilliseconds, tags);
    }

    public void PaymentProcessingFailed(Payment payment, string reason, TimeSpan duration)
    {
        Interlocked.Decrement(ref _currentActiveProcessing);

        var outcomeTags = CreatePaymentTags(payment, outcome: FailedOutcome);
        var failureTags = CreatePaymentTags(payment, reason: reason);

        _paymentProcessed.Add(1, outcomeTags);
        _paymentFailed.Add(1, failureTags);
        _paymentValue.Add((double)payment.Amount, outcomeTags);
        _paymentProcessingDuration.Record(duration.TotalMilliseconds, outcomeTags);
    }

    public void BankRequestCompleted(Payment payment, string provider, bool succeeded, TimeSpan duration)
    {
        var tags = CreatePaymentTags(
            payment,
            provider: provider,
            outcome: succeeded ? SuccessOutcome : FailedOutcome);

        _bankRequestDuration.Record(duration.TotalMilliseconds, tags);
        _bankRequestValue.Add((double)payment.Amount, tags);
    }
    
    /// <summary>
    /// Добавляем дополнительные labels для метрик, чтобы иметь возможность создать более детальный дашборд,
    /// не создавая миллион однотипных метрик под незначительно отличающийся контекст.
    /// </summary>
    /// <returns>TagList - легковесная структура, не занимающая heap</returns>
    private static TagList CreatePaymentTags(
        Payment payment,
        string? provider = null,
        string? outcome = null,
        string? reason = null)
    {
        // Labels должны быть низкокардинальными. Currency/provider/outcome/reason подходят для этого.
        // ВАЖНО! Не добавляем payment.Id, userId, requestId и другие уникальные значения: они взорвут cardinality в Prometheus.
        // Prometheus сохраняет временной ряд для каждого тэга. Если уникальных тэгов станет слишком много - память будет исчерпана.
        // Тэги должны быть категориями, по которым группируются данные, а не уникальными идентификаторами.
        var tags = new TagList
        {
            { CurrencyTag, payment.Currency }
        };

        if (!string.IsNullOrWhiteSpace(provider))
        {
            tags.Add(ProviderTag, provider);
        }

        if (!string.IsNullOrWhiteSpace(outcome))
        {
            tags.Add(OutcomeTag, outcome);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            tags.Add(ReasonTag, reason);
        }

        return tags;
    }
}
