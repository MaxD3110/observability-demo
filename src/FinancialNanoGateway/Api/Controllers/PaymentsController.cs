using FinancialNanoGateway.Application;
using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;
using FinancialNanoGateway.Application.Dtos.Response;
using FinancialNanoGateway.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinancialNanoGateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentQueue _paymentQueue;
    private readonly IPaymentMetrics _metrics;
    private readonly IPaymentTracing _tracing;

    public PaymentsController(IPaymentQueue paymentQueue, IPaymentMetrics metrics, IPaymentTracing tracing)
    {
        _paymentQueue = paymentQueue;
        _metrics = metrics;
        _tracing = tracing;
    }

    /// <summary>
    /// Создает запрос на оплату и передает в обработку.
    /// </summary>
    /// <returns>Id запроса и номер в очереди</returns>
    /// <response code="202">Запрос на оплату добавлен в очередь</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePayment(
        PaymentRequestDto requestDto,
        CancellationToken cancellationToken)
    {
        if (requestDto.Amount <= 0)
            return BadRequest("Amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(requestDto.Currency))
            return BadRequest("Currency is required.");

        var payment = new Payment
        {
            Amount = requestDto.Amount,
            Currency = requestDto.Currency.Trim().ToUpperInvariant()
        };

        _metrics.PaymentRequested(payment);

        // Заголовки сообщения - наш carrier для trace-контекста. StartPublish положит сюда traceparent.
        var headers = new Dictionary<string, string>();
        using (_tracing.StartPublish(payment, headers))
        {
            await _paymentQueue.EnqueueAsync(new PaymentMessageEnvelopeDto(payment, headers), cancellationToken);
        }

        return Accepted(new PaymentResponseDto(payment.Id, "Queued", _paymentQueue.Count));
    }
}
