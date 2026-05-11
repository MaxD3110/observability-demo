namespace FinancialNanoGateway.Infrastructure.Options;

public sealed class BankBIntegrationOptions
{
    public required string ProviderName { get; init; }

    public int FailureRatePercentage { get; init; }

    public int MinRequestDelayMs { get; init; }

    public int MaxRequestDelayMs { get; init; }
}
