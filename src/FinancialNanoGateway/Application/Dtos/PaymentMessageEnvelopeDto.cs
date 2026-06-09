using FinancialNanoGateway.Domain.Models;

namespace FinancialNanoGateway.Application.Dtos;

/// <summary>
/// Message envelope on the queue: payload + headers.
/// </summary>
public sealed record PaymentMessageEnvelopeDto(Payment Payment, Dictionary<string, string> Headers);
