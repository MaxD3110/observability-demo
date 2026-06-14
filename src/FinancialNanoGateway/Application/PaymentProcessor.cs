using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Logging;
using System.Diagnostics;
using FinancialNanoGateway.Application.Dtos;

namespace FinancialNanoGateway.Application;

public sealed class PaymentProcessor : BackgroundService
{
    private readonly IPaymentQueue _paymentQueue;
    private readonly IPaymentMetrics _metrics;
    private readonly IPaymentTracing _tracing;
    private readonly ILogger<PaymentProcessor> _logger;
    private readonly IBankIntegrationService[] _bankIntegrationServices;

    public PaymentProcessor(
        IPaymentQueue paymentQueue,
        IEnumerable<IBankIntegrationService> bankIntegrationServices,
        IPaymentMetrics metrics,
        IPaymentTracing tracing,
        ILogger<PaymentProcessor> logger)
    {
        _paymentQueue = paymentQueue;
        _bankIntegrationServices = bankIntegrationServices.ToArray();
        _metrics = metrics;
        _tracing = tracing;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _paymentQueue.ReadAllAsync(cancellationToken))
        {
            await ProcessPaymentAsync(message, cancellationToken);
        }
    }

    private async Task ProcessPaymentAsync(PaymentMessageEnvelopeDto messageEnvelopeDto, CancellationToken cancellationToken)
    {
        var payment = messageEnvelopeDto.Payment;
        var stopwatch = Stopwatch.StartNew();

        // CONSUMER span: a new trace linked to the producer (context is taken from the headers).
        using var activity = _tracing.StartProcess(payment, messageEnvelopeDto.Headers);

        // Scope attaches PaymentId/Currency to ALL logs inside (including the bank service's logs),
        // without passing them explicitly into every call. With IncludeScopes they become log attributes in Loki.
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["PaymentId"] = payment.Id,
            ["Currency"] = payment.Currency
        });

        _metrics.PaymentProcessingStarted(payment);

        try
        {
            var index = Random.Shared.Next(_bankIntegrationServices.Length);
            var bank = _bankIntegrationServices[index];

            // A span EVENT is a timestamped moment INSIDE the span (when something happened),
            // distinct from a TAG, which is an attribute describing the whole span. (AddException
            // below is just a special, well-known event - this shows the general form.)
            activity?.AddEvent(new ActivityEvent(
                "bank.dispatch",
                tags: new ActivityTagsCollection { ["bank.provider"] = bank.GetType().Name }));

            await bank.ProcessPaymentAsync(payment, cancellationToken);

            _metrics.PaymentProcessingCompleted(payment, stopwatch.Elapsed);
            PaymentLog.PaymentProcessed(_logger, payment.Id, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            _metrics.PaymentProcessingFailed(payment, exception.GetType().Name, stopwatch.Elapsed);
            PaymentLog.PaymentProcessingFailed(_logger, exception, payment.Id, exception.GetType().Name);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
