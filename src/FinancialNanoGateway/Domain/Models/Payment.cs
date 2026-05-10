namespace FinancialNanoGateway.Domain.Models;

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}