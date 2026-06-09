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

        // CONSUMER-span: новый trace, связанный link-ом с producer-ом (контекст берется из заголовков).
        using var activity = _tracing.StartProcess(payment, messageEnvelopeDto.Headers);

        // Scope добавляет PaymentId/Currency ко ВСЕМ логам внутри (включая логи банковского сервиса),
        // не таская их явно в каждый вызов. С IncludeScopes они становятся атрибутами лога в Loki.
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["PaymentId"] = payment.Id,
            ["Currency"] = payment.Currency
        });

        _metrics.PaymentProcessingStarted(payment);

        try
        {
            var index = Random.Shared.Next(_bankIntegrationServices.Length);
            await _bankIntegrationServices[index].ProcessPaymentAsync(payment, cancellationToken);

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
