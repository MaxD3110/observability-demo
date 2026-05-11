namespace FinancialNanoGateway.Application.Dtos.Response;

public sealed record PaymentResponseDto(Guid Id, string Status, int QueueLength);