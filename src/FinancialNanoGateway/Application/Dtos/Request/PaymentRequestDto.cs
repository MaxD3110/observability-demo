namespace FinancialNanoGateway.Application.Dtos;

public sealed record PaymentRequestDto(decimal Amount, string Currency);
