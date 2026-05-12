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
    private readonly IPaymentMetrics _metrics;
    private readonly ILogger<PaymentProcessor> _logger;
    private readonly IBankIntegrationService[] _bankIntegrationServices;

    public PaymentProcessor(
        IPaymentQueue paymentQueue,
        IEnumerable<IBankIntegrationService> bankIntegrationServices,
        IPaymentMetrics metrics,
        ILogger<PaymentProcessor> logger)
    {
        _paymentQueue = paymentQueue;
        _bankIntegrationServices = bankIntegrationServices.ToArray();
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
            var index = Random.Shared.Next(_bankIntegrationServices.Length);
            await _bankIntegrationServices[index].ProcessPaymentAsync(payment, cancellationToken);

            _metrics.PaymentProcessingCompleted(payment, stopwatch.Elapsed);
            _logger.LogInformation("Payment processed. PaymentId={PaymentId}", payment.Id);
        }
        catch (Exception exception)
        {
            _metrics.PaymentProcessingFailed(payment, exception.GetType().Name, stopwatch.Elapsed);
            _logger.LogWarning(exception, "Payment failed. PaymentId={PaymentId}", payment.Id);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
