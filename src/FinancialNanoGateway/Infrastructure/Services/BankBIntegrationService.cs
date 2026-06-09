using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Exceptions;
using FinancialNanoGateway.Domain.Models;
using FinancialNanoGateway.Infrastructure.Options;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace FinancialNanoGateway.Infrastructure.Services;

public sealed class BankBIntegrationService : IBankIntegrationService
{
    private readonly IOptionsMonitor<BankBIntegrationOptions> _options;
    private readonly IPaymentMetrics _metrics;
    private readonly IPaymentTracing _tracing;

    public BankBIntegrationService(
        IOptionsMonitor<BankBIntegrationOptions> options,
        IPaymentMetrics metrics,
        IPaymentTracing tracing)
    {
        _options = options;
        _metrics = metrics;
        _tracing = tracing;
    }

    public async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        using var activity = _tracing.StartBankRequest(payment, _options.CurrentValue.ProviderName);
        
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
