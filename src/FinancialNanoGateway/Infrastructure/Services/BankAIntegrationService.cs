using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Logging;
using FinancialNanoGateway.Domain.Exceptions;
using FinancialNanoGateway.Domain.Models;
using FinancialNanoGateway.Infrastructure.Options;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace FinancialNanoGateway.Infrastructure.Services;

public sealed class BankAIntegrationService : IBankIntegrationService
{
    private readonly IOptionsMonitor<BankAIntegrationOptions> _options;
    private readonly IPaymentMetrics _metrics;
    private readonly IPaymentTracing _tracing;
    private readonly ILogger<BankAIntegrationService> _logger;

    public BankAIntegrationService(
        IOptionsMonitor<BankAIntegrationOptions> options,
        IPaymentMetrics metrics,
        IPaymentTracing tracing,
        ILogger<BankAIntegrationService> logger)
    {
        _options = options;
        _metrics = metrics;
        _tracing = tracing;
        _logger = logger;
    }

    public async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        using var activity = _tracing.StartBankRequest(payment, _options.CurrentValue.ProviderName);
        PaymentLog.BankRequestStarting(_logger, payment.Id, _options.CurrentValue.ProviderName);

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        try
        {
            var latency = Random.Shared.Next(_options.CurrentValue.MinRequestDelayMs, _options.CurrentValue.MaxRequestDelayMs + 1);
            await Task.Delay(latency, cancellationToken);

            var failed = Random.Shared.NextInt64(100) < _options.CurrentValue.FailureRatePercentage;
            if (failed)
            {
                Action[] pitfalls =
                [
                    () => throw new BankUnavailableException(),
                    () => throw new PaymentFailedException(payment.Id)
                ];

                pitfalls[Random.Shared.Next(pitfalls.Length)]();
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.BankRequestCompleted(payment, _options.CurrentValue.ProviderName, succeeded, stopwatch.Elapsed);
        }
    }
}
