using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Dtos;

/// <summary>
/// Конверт (envelope) сообщения в очереди: полезная нагрузка + заголовки.
/// </summary>
public sealed record PaymentMessageEnvelopeDto(Payment Payment, Dictionary<string, string> Headers);
