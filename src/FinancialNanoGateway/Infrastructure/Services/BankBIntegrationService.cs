using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Domain.Exceptions;
using FinancialNanoGateway.Domain.Models;
using FinancialNanoGateway.Infrastructure.Options;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace FinancialNanoGateway.Infrastructure.Services;

public sealed class BankBIntegrationService : IBankIntegrationService
{
    private readonly BankBIntegrationOptions _options;
    private readonly IPaymentMetrics _metrics;

    public BankBIntegrationService(IOptions<BankBIntegrationOptions> options, IPaymentMetrics metrics)
    {
        _options = options.Value;
        _metrics = metrics;
    }
    
    public async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;

        try
        {
            var latency = Random.Shared.Next(_options.MinRequestDelayMs, _options.MaxRequestDelayMs + 1);
            await Task.Delay(latency, cancellationToken);

            var failed = Random.Shared.NextInt64(100) < _options.FailureRatePercentage;
            if (failed)
            {
                throw new PaymentFailedException(payment.Id);
            }

            succeeded = true;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.BankRequestCompleted(payment, _options.ProviderName, succeeded, stopwatch.Elapsed);
        }
    }
}
