using System.Diagnostics;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Models;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace FinancialNanoGateway.Infrastructure.Observability;

public sealed class PaymentTracing : IPaymentTracing
{
    public const string ActivitySourceName = "FinancialNanoGateway.Payments";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    // The propagator serializes/deserializes the trace context to/from text headers.
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    // The getter/setter describe HOW to put and read headers in our "broker" (Dictionary).
    // With a real broker this would write/read Kafka message headers or RabbitMQ properties.
    private static readonly Action<IDictionary<string, string>, string, string> InjectHeader =
        (headers, key, value) => headers[key] = value;

    private static readonly Func<IReadOnlyDictionary<string, string>, string, IEnumerable<string>> ExtractHeader =
        (headers, key) => headers.TryGetValue(key, out var value) ? [value] : [];

    private const string DestinationName = "payments";
    private const string MessagingSystem = "dotnet.channel";

    public Activity? StartPublish(Payment payment, IDictionary<string, string> headers)
    {
        // The PRODUCER span is created INSIDE the request's server span (Activity.Current), so it
        // becomes its child and lands in the same trace as the HTTP POST.
        var activity = Source.StartActivity("payments publish", ActivityKind.Producer);

        SetMessagingTags(activity, operation: "publish");
        SetPaymentTags(activity, payment);

        // The key moment: we put the CURRENT span's context into the message headers.
        var contextToInject = activity?.Context ?? Activity.Current?.Context ?? default;
        Propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), headers, InjectHeader);

        return activity;
    }

    public Activity? StartProcess(Payment payment, IReadOnlyDictionary<string, string> headers)
    {
        // Extract the producer context from the message headers (different thread, context no longer ambient).
        var parentContext = Propagator.Extract(default, headers, ExtractHeader);
        Baggage.Current = parentContext.Baggage;

        // Best practice for queues: the consumer starts a NEW trace and LINKS it to the producer,
        // instead of making it parent-child. Reason: a consumer may drain the queue in batches / with delay,
        // and its lifecycle is not nested in the request. parentContext: default => new trace; links => link to publish.
        var links = new[] { new ActivityLink(parentContext.ActivityContext) };
        var activity = Source.StartActivity(
            "payments process",
            ActivityKind.Consumer,
            parentContext: default,
            tags: null,
            links: links);

        SetMessagingTags(activity, operation: "process");
        SetPaymentTags(activity, payment);

        return activity;
    }

    public Activity? StartBankRequest(Payment payment, string provider)
    {
        var activity = Source.StartActivity($"bank {provider}", ActivityKind.Client);

        activity?.SetTag("payment.provider", provider);
        activity?.SetTag("peer.service", provider);
        SetPaymentTags(activity, payment);

        return activity;
    }

    private static void SetMessagingTags(Activity? activity, string operation)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("messaging.system", MessagingSystem);
        activity.SetTag("messaging.destination.name", DestinationName);
        activity.SetTag("messaging.operation.type", operation);
    }

    private static void SetPaymentTags(Activity? activity, Payment payment)
    {
        if (activity is null)
        {
            return;
        }

        // IMPORTANT (and the opposite of metrics!): in traces, high cardinality is good.
        // In PaymentMetrics we did NOT add payment.Id/amount, to avoid exploding the number of time series in Prometheus.
        // Here a span is one specific request, not an aggregate. The more context per request,
        // the faster the debugging. So we deliberately add the unique fields: id, exact amount.
        activity.SetTag("messaging.message.id", payment.Id);
        activity.SetTag("payment.id", payment.Id);
        activity.SetTag("payment.currency", payment.Currency);
        activity.SetTag("payment.amount", payment.Amount);
    }
}
