using FinancialNanoGateway.Application.Abstractions;
using FinancialNanoGateway.Application.Dtos;
using FinancialNanoGateway.Application.Dtos.Response;
using FinancialNanoGateway.Application.Logging;
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
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentQueue paymentQueue,
        IPaymentMetrics metrics,
        IPaymentTracing tracing,
        ILogger<PaymentsController> logger)
    {
        _paymentQueue = paymentQueue;
        _metrics = metrics;
        _tracing = tracing;
        _logger = logger;
    }

    /// <summary>
    /// Creates a payment request and hands it off for processing.
    /// </summary>
    /// <returns>The request id and its position in the queue.</returns>
    /// <response code="202">The payment request was added to the queue.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePayment(
        PaymentRequestDto requestDto,
        CancellationToken cancellationToken)
    {
        var rejectReason = ValidateRequest(requestDto);

        if (!string.IsNullOrEmpty(rejectReason))
        {
            // Plain structured logging: the same named-placeholder idea as PaymentLog, but written by
            // hand. PaymentLog shows the source-generated, allocation-free version used on hot paths.
            _logger.LogError("Payment rejected. Reason={Reason}", rejectReason);
            return BadRequest(rejectReason);
        }

        var payment = new Payment
        {
            Amount = requestDto.Amount,
            Currency = requestDto.Currency.Trim().ToUpperInvariant()
        };

        _metrics.PaymentRequested(payment);

        // The message headers are our carrier for the trace context. StartPublish puts the traceparent here.
        var headers = new Dictionary<string, string>();
        using (_tracing.StartPublish(payment, headers))
        {
            await _paymentQueue.EnqueueAsync(new PaymentMessageEnvelopeDto(payment, headers), cancellationToken);
        }

        PaymentLog.PaymentAccepted(_logger, payment.Id, payment.Currency);

        return Accepted(new PaymentResponseDto(payment.Id, "Queued", _paymentQueue.Count));
    }

    private static string? ValidateRequest(PaymentRequestDto requestDto)
    {
        if (requestDto.Amount <= 0)
            return "Amount must be greater than zero.";

        if (string.IsNullOrWhiteSpace(requestDto.Currency))
            return "Currency is required.";

        return null;
    }
}
