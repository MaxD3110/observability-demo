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

    // Propagator сериализует/десериализует trace-контекст в текстовые заголовки
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    // Геттер/сеттер описывают, КАК класть и читать заголовки в нашем "брокере" (Dictionary).
    // С реальным брокером тут была бы запись/чтение message headers Kafka или properties RabbitMQ.
    private static readonly Action<IDictionary<string, string>, string, string> InjectHeader =
        (headers, key, value) => headers[key] = value;

    private static readonly Func<IReadOnlyDictionary<string, string>, string, IEnumerable<string>> ExtractHeader =
        (headers, key) => headers.TryGetValue(key, out var value) ? [value] : [];

    private const string DestinationName = "payments";
    private const string MessagingSystem = "dotnet.channel";

    public Activity? StartPublish(Payment payment, IDictionary<string, string> headers)
    {
        // PRODUCER-span создается ВНУТРИ серверного span-а запроса (Activity.Current), поэтому
        // становится его child-ом и попадает в тот же trace, что и HTTP POST.
        var activity = Source.StartActivity("payments publish", ActivityKind.Producer);

        SetMessagingTags(activity, operation: "publish");
        SetPaymentTags(activity, payment);

        // Ключевой момент: кладем контекст ТЕКУЩЕГО span-а в заголовки сообщения.
        var contextToInject = activity?.Context ?? Activity.Current?.Context ?? default;
        Propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), headers, InjectHeader);

        return activity;
    }

    public Activity? StartProcess(Payment payment, IReadOnlyDictionary<string, string> headers)
    {
        // Достаем контекст producer-а из заголовков сообщения (другой поток, контекст уже не в ambient).
        var parentContext = Propagator.Extract(default, headers, ExtractHeader);
        Baggage.Current = parentContext.Baggage;

        // Best practice для очередей: consumer начинает НОВЫЙ trace и СВЯЗЫВАЕТ его link-ом с producer-ом,
        // а не делает parent-child. Причина: consumer может разгребать очередь пачкой/с задержкой, и его
        // жизненный цикл не вложен в запрос. parentContext: default => новый trace; links => связь с publish.
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

        // ВАЖНО (и противоположно метрикам!): в трейсах высокая кардинальность.
        // В PaymentMetrics мы НЕ добавляли payment.Id/amount, чтобы не взорвать кол-во time-series в Prometheus.
        // Здесь же span - это один конкретный запрос, а не агрегат. Чем больше контекста на запрос,
        // тем быстрее дебаг. Поэтому кладем именно уникальные поля: id, точную сумму.
        activity.SetTag("messaging.message.id", payment.Id);
        activity.SetTag("payment.id", payment.Id);
        activity.SetTag("payment.currency", payment.Currency);
        activity.SetTag("payment.amount", payment.Amount);
    }
}
