using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Exceptions;
using FinancialNanoGateway.Domain.Models;
using System.Diagnostics;
using FinancialNanoGateway.Application.Dtos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        _metrics.PaymentProcessingStarted(payment);

        try
        {
            var index = Random.Shared.Next(_bankIntegrationServices.Length);
            await _bankIntegrationServices[index].ProcessPaymentAsync(payment, cancellationToken);

            _metrics.PaymentProcessingCompleted(payment, stopwatch.Elapsed);
            _logger.LogInformation("Payment processed. PaymentId={PaymentId}", payment.Id);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            _metrics.PaymentProcessingFailed(payment, exception.GetType().Name, stopwatch.Elapsed);
            _logger.LogWarning(exception, "Payment failed. PaymentId={PaymentId}", payment.Id);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
