namespace FinancialNanoGateway.Application.Dtos;

/// <summary>
/// Request body for creating a payment.
/// </summary>
/// <param name="Amount">Payment amount in the requested currency.</param>
/// <param name="Currency">ISO-like currency code, for example USD or EUR.</param>
public sealed record PaymentRequestDto(decimal Amount, string Currency);
