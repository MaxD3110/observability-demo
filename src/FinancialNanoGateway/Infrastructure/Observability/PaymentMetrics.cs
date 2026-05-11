using System.Diagnostics;
using System.Diagnostics.Metrics;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Infrastructure.Observability;

public sealed class PaymentMetrics : IPaymentMetrics
{
    public const string MeterName = "FinancialNanoGateway.Payments";

    private readonly Counter<long> _paymentRequests;
    private readonly Counter<long> _paymentEnqueued;
    private readonly Counter<long> _paymentProcessed;
    private readonly Counter<long> _paymentFailed;
    private readonly Counter<double> _paymentValue;
    private readonly UpDownCounter<int> _queueChanges;
    private readonly UpDownCounter<int> _activeProcessingChanges;
    private readonly Histogram<double> _paymentAmount;
    private readonly Histogram<double> _paymentProcessingDuration;
    private readonly Histogram<double> _bankRequestDuration;
    private readonly ObservableGauge<int> _queueDepth;
    private readonly ObservableUpDownCounter<int> _activeProcessing;
    private readonly ObservableCounter<long> _lifetimeProcessed;

    private int _currentQueueDepth;
    private int _currentActiveProcessing;
    private long _processedPayments;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        
        _paymentRequests = meter.CreateCounter<long>(
            "payment_requests",
            description: "Number of payment requests accepted by the API layer.");

        _paymentEnqueued = meter.CreateCounter<long>(
            "payment_enqueued",
            description: "Number of payments successfully added to the processing queue.");

        _paymentProcessed = meter.CreateCounter<long>(
            "payment_processed",
            description: "Number of payments processed by the background worker.");

        _paymentFailed = meter.CreateCounter<long>(
            "payment_failed",
            description: "Number of payment processing failures.");

        _paymentValue = meter.CreateCounter<double>(
            "payment_value",
            description: "Total monetary value of successfully processed payments.");

        _queueChanges = meter.CreateUpDownCounter<int>(
            "payment_queue_changes",
            description: "Queue length changes. Positive values enqueue payments; negative values dequeue them.");

        _activeProcessingChanges = meter.CreateUpDownCounter<int>(
            "payment_active_processing_changes",
            description: "Active payment processing changes.");

        _paymentAmount = meter.CreateHistogram<double>(
            "payment_amount",
            description: "Distribution of requested payment amounts.");

        _paymentProcessingDuration = meter.CreateHistogram<double>(
            "payment_processing_duration_ms",
            description: "End-to-end background processing duration in milliseconds.");

        _bankRequestDuration = meter.CreateHistogram<double>(
            "bank_request_duration_ms",
            description: "Mock bank request duration in milliseconds.");

        _queueDepth = meter.CreateObservableGauge(
            "payment_queue_depth",
            () => Volatile.Read(ref _currentQueueDepth),
            description: "Current number of payments waiting in the queue.");

        _activeProcessing = meter.CreateObservableUpDownCounter(
            "payment_active_processing",
            () => Volatile.Read(ref _currentActiveProcessing),
            description: "Current number of payments being processed.");

        _lifetimeProcessed = meter.CreateObservableCounter(
            "payment_lifetime_processed",
            () => Volatile.Read(ref _processedPayments),
            description: "Current process lifetime count of processed payments.");
    }

    public void PaymentRequested(Payment payment)
    {
        var tags = Currency(payment);

        _paymentRequests.Add(1, tags);
        _paymentAmount.Record((double)payment.Amount, tags);
    }

    public void PaymentEnqueued(Payment payment)
    {
        Interlocked.Increment(ref _currentQueueDepth);

        _paymentEnqueued.Add(1, Currency(payment));
        _queueChanges.Add(1);
    }

    public void PaymentDequeued(Payment payment)
    {
        Interlocked.Decrement(ref _currentQueueDepth);
        _queueChanges.Add(-1);
    }

    public void PaymentProcessingStarted(Payment payment)
    {
        Interlocked.Increment(ref _currentActiveProcessing);
        _activeProcessingChanges.Add(1);
    }

    public void PaymentProcessingCompleted(Payment payment, TimeSpan duration)
    {
        Interlocked.Decrement(ref _currentActiveProcessing);
        Interlocked.Increment(ref _processedPayments);

        _activeProcessingChanges.Add(-1);
        _paymentProcessed.Add(1, WithStatus(Currency(payment), "success"));
        _paymentValue.Add((double)payment.Amount, Currency(payment));
        _paymentProcessingDuration.Record(duration.TotalMilliseconds, WithStatus(Currency(payment), "success"));
    }

    public void PaymentProcessingFailed(Payment payment, string reason, TimeSpan duration)
    {
        Interlocked.Decrement(ref _currentActiveProcessing);
        Interlocked.Increment(ref _processedPayments);

        _activeProcessingChanges.Add(-1);
        _paymentProcessed.Add(1, WithStatus(Currency(payment), "failed"));
        _paymentFailed.Add(1, WithReason(Currency(payment), reason));
        _paymentProcessingDuration.Record(duration.TotalMilliseconds, WithStatus(Currency(payment), "failed"));
    }

    public void BankRequestCompleted(Payment payment, string provider, bool succeeded, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "currency", payment.Currency },
            { "provider", provider },
            { "outcome", succeeded ? "success" : "failed" }
        };

        _bankRequestDuration.Record(duration.TotalMilliseconds, tags);
    }

    private static TagList Currency(Payment payment) =>
        new()
        {
            { "currency", payment.Currency }
        };

    private static TagList WithStatus(TagList tags, string status)
    {
        tags.Add("status", status);
        return tags;
    }

    private static TagList WithReason(TagList tags, string reason)
    {
        tags.Add("reason", reason);
        return tags;
    }
}
