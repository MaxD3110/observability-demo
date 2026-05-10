using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;
using FinancialNanoGateway.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinancialNanoGateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentQueue _paymentQueue;
    private readonly IPaymentMetrics _metrics;

    public PaymentsController(IPaymentQueue paymentQueue, IPaymentMetrics metrics)
    {
        _paymentQueue = paymentQueue;
        _metrics = metrics;
    }

    /// <summary>
    /// Queues a payment for asynchronous bank processing.
    /// </summary>
    /// <param name="requestDto">Payment amount and currency.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Accepted response with the payment id and current queue length.</returns>
    /// <response code="202">The payment was queued.</response>
    /// <response code="400">The payment request is invalid.</response>
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
        await _paymentQueue.EnqueueAsync(payment, cancellationToken);

        return Accepted(new
        {
            payment.Id,
            Status = "Queued",
            QueueLength = _paymentQueue.Count
        });
    }
}
