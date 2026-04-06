using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

/// <summary>
/// POS payment endpoints for restaurant transactions.
/// Not related to TenantSubscription (app feature billing).
/// </summary>
[Route("api/pos-payments")]
[ApiController]
[Authorize]
public class PosPaymentsController : ControllerBase
{
    private readonly IPosPaymentService _paymentService;

    public PosPaymentsController(IPosPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("cash/{orderId:guid}")]
    [Authorize(Policy = PolicyNames.CanManagePosPayments)]
    public async Task<ActionResult<PosPayment>> CreateCashPayment(Guid orderId)
    {
        try
        {
            var payment = await _paymentService.CreateCashPaymentAsync(orderId);
            return Ok(payment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("stripe/{orderId:guid}")]
    [Authorize(Policy = PolicyNames.CanManagePosPayments)]
    public async Task<ActionResult> CreateStripeCheckout(Guid orderId, CreateStripePaymentDto dto)
    {
        try
        {
            var (payment, checkoutUrl) = await _paymentService.CreateStripeCheckoutAsync(
                orderId, dto.SuccessUrl, dto.CancelUrl);
            return Ok(new { payment.Id, CheckoutUrl = checkoutUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        // Read raw body for Stripe signature verification
        string json;
        using (var reader = new StreamReader(HttpContext.Request.Body))
        {
            json = await reader.ReadToEndAsync();
        }

        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
            return BadRequest("Missing Stripe-Signature header.");

        try
        {
            await _paymentService.HandleWebhookAsync(json, signature);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = PolicyNames.CanManagePosPayments)]
    public async Task<ActionResult<PosPayment>> Refund(Guid id, RefundPaymentDto? dto = null)
    {
        try
        {
            var payment = await _paymentService.RefundAsync(id, dto?.Amount, dto?.Reason);
            return Ok(payment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("order/{orderId:guid}")]
    [Authorize(Policy = PolicyNames.CanViewPosPayments)]
    public async Task<ActionResult<PosPayment>> GetByOrder(Guid orderId)
    {
        var payment = await _paymentService.GetByOrderIdAsync(orderId);
        if (payment is null)
            return NotFound();
        return Ok(payment);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewPosPayments)]
    public async Task<ActionResult<PosPayment>> Get(Guid id)
    {
        var payment = await _paymentService.GetByIdAsync(id);
        if (payment is null)
            return NotFound();
        return Ok(payment);
    }
}
