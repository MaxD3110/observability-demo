using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Exceptions;
using FinancialNanoGateway.Domain.Models;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialNanoGateway.Application;

public sealed class PaymentProcessor : BackgroundService
{
    private readonly IPaymentQueue _paymentQueue;
    private readonly IBankIntegrationService _bankIntegrationService;
    private readonly IPaymentMetrics _metrics;
    private readonly ILogger<PaymentProcessor> _logger;

    public PaymentProcessor(
        IPaymentQueue paymentQueue,
        IBankIntegrationService bankIntegrationService,
        IPaymentMetrics metrics,
        ILogger<PaymentProcessor> logger)
    {
        _paymentQueue = paymentQueue;
        _bankIntegrationService = bankIntegrationService;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var payment in _paymentQueue.ReadAllAsync(cancellationToken))
        {
            await ProcessPaymentAsync(payment, cancellationToken);
        }
    }

    private async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _metrics.PaymentProcessingStarted(payment);

        try
        {
            await _bankIntegrationService.ProcessPaymentAsync(payment, cancellationToken);

            _metrics.PaymentProcessingCompleted(payment, stopwatch.Elapsed);
            _logger.LogInformation("Payment processed. PaymentId={PaymentId}", payment.Id);
        }
        catch (PaymentFailedException exception)
        {
            _metrics.PaymentProcessingFailed(payment, "bank_declined", stopwatch.Elapsed);
            _logger.LogWarning(exception, "Payment failed. PaymentId={PaymentId}", payment.Id);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
