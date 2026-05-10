namespace FinancialNanoGateway.Infrastructure.Options;

public sealed class BankIntegrationOptions
{
    public const string SectionName = "BankIntegrationOptions";

    public string ProviderName { get; init; } = "MockBank";

    public int FailureRatePercentage { get; init; } = 10;

    public int MinRequestDelayMs { get; init; } = 50;

    public int MaxRequestDelayMs { get; init; } = 500;
}
