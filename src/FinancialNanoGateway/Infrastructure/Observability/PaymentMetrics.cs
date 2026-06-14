using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;
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
    private readonly UpDownCounter<int> _activeProcessing;

    // Observable instrument: its callback is pulled at scrape time, so the reference must live
    // as long as the Meter even though we never touch it directly again.
    private readonly ObservableGauge<int> _queueDepth;

    public PaymentMetrics(IMeterFactory meterFactory, ChannelReader<PaymentMessageEnvelopeDto> queueReader)
    {
        var meter = meterFactory.Create(MeterName);

        // Counter fits events that only ever increase.
        _paymentRequests = meter.CreateCounter<long>(
            "payment_requests",
            unit: "{payment}",
            description: "Number of valid payment requests accepted by the API.");

        _paymentEnqueued = meter.CreateCounter<long>(
            "payment_enqueued",
            unit: "{payment}",
            description: "Number of payments successfully placed on the queue.");

        _paymentProcessed = meter.CreateCounter<long>(
            "payment_processed",
            unit: "{payment}",
            description: "Number of payments processed by the background worker.");

        _paymentFailed = meter.CreateCounter<long>(
            "payment_failed",
            unit: "{payment}",
            description: "Number of payments that ended in an error.");

        _paymentValue = meter.CreateCounter<double>(
            "payment_value",
            description: "Total value of payments by processing outcome. Currency and outcome are passed as labels.");

        _bankRequestValue = meter.CreateCounter<double>(
            "bank_request_value",
            description: "Total value of payments by bank provider and call outcome.");

        // Histogram is for distributions: it yields p50/p95/p99, not just the average.
        _paymentAmount = meter.CreateHistogram<double>(
            "payment_amount",
            description: "Distribution of incoming payment amounts. Currency is passed as a label.");

        _paymentQueueWaitDuration = meter.CreateHistogram<double>(
            "payment_queue_wait_duration",
            unit: "ms",
            description: "How long a payment spent in the queue before processing started.");

        _paymentProcessingDuration = meter.CreateHistogram<double>(
            "payment_processing_duration",
            unit: "ms",
            description: "Full duration of payment processing by the background worker.");

        _bankRequestDuration = meter.CreateHistogram<double>(
            "bank_request_duration",
            unit: "ms",
            description: "Duration of the call to the bank provider.");

        // Two ways to track a "current level", and the demo shows both side by side:
        //
        // ObservableGauge = PULL. The SDK invokes this callback at scrape time to READ state we have
        // no event for. The channel already knows its own length, so we just sample Reader.Count -
        // no per-change bookkeeping. This is the right tool when you can read a value on demand but
        // don't have (or don't want to instrument) the individual events that change it.
        _queueDepth = meter.CreateObservableGauge(
            "payment_queue_depth",
            () => queueReader.CanCount ? queueReader.Count : 0,
            unit: "{payment}",
            description: "Current number of payments waiting to be processed.");

        // UpDownCounter = PUSH. We record +1/-1 inline because we ARE standing at the start/end
        // events (PaymentProcessingStarted/Completed/Failed). When you already sit on the events,
        // a synchronous instrument is simpler than maintaining state behind an observable callback.
        _activeProcessing = meter.CreateUpDownCounter<int>(
            "payment_active_processing",
            unit: "{payment}",
            description: "Current number of payments being processed right now.");
    }

    public void PaymentRequested(Payment payment)
    {
        var tags = CreatePaymentTags(payment);

        _paymentRequests.Add(1, tags);
        _paymentAmount.Record((double)payment.Amount, tags);
    }

    public void PaymentEnqueued(Payment payment)
    {
        // No queue-depth bookkeeping here: the observable gauge reads the channel's own count.
        _paymentEnqueued.Add(1, CreatePaymentTags(payment));
    }

    public void PaymentDequeued(Payment payment)
    {
        var queueWaitDuration = DateTime.UtcNow - payment.CreatedAt;
        if (queueWaitDuration >= TimeSpan.Zero)
        {
            _paymentQueueWaitDuration.Record(queueWaitDuration.TotalMilliseconds, CreatePaymentTags(payment));
        }
    }

    public void PaymentProcessingStarted(Payment payment)
    {
        _activeProcessing.Add(1);
    }

    public void PaymentProcessingCompleted(Payment payment, TimeSpan duration)
    {
        _activeProcessing.Add(-1);

        var tags = CreatePaymentTags(payment, outcome: SuccessOutcome);
        _paymentProcessed.Add(1, tags);
        _paymentValue.Add((double)payment.Amount, tags);
        _paymentProcessingDuration.Record(duration.TotalMilliseconds, tags);
    }

    public void PaymentProcessingFailed(Payment payment, string reason, TimeSpan duration)
    {
        _activeProcessing.Add(-1);

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
    /// Adds extra labels to metrics so we can build a more detailed dashboard
    /// without creating a million near-identical metrics for slightly different context.
    /// Example: payments_total{currency="USD", status="success", provider="stripe"}
    /// </summary>
    /// <returns>TagList - a lightweight struct that does not allocate on the heap.</returns>
    private static TagList CreatePaymentTags(
        Payment payment,
        string? provider = null,
        string? outcome = null,
        string? reason = null)
    {
        // Labels must be low-cardinality. Currency/provider/outcome/reason are a good fit.
        // IMPORTANT! Do not add payment.Id, userId, requestId or other unique values: they explode cardinality in Prometheus.
        // Prometheus stores a time series per unique tag set. Too many unique tags and memory runs out.
        // Tags should be categories you group data by, not unique identifiers.
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
