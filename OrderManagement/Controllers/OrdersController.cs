using Microsoft.AspNetCore.Mvc;
using OrderManagement.DTOs;
using OrderManagement.Enums;
using OrderManagement.Services;

namespace OrderManagement.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
    [FromBody] CreateOrderRequest request,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return UnprocessableEntity(new
            {
                detail = "Idempotency-Key header is required."
            });

        var result = await orderService.CreateAsync(
            request,
            idempotencyKey,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            result);
    }



    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await orderService.GetByIdAsync(id, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { detail = "page must be >= 1 and pageSize must be between 1 and 100." });

        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            return BadRequest(new { detail = "fromDate must not be later than toDate." });

        return Ok(await orderService.ListAsync(status, customerId, fromDate, toDate, page, pageSize, cancellationToken));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
        => Ok(await orderService.UpdateStatusAsync(id, request.Status, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        => Ok(await orderService.CancelAsync(id, cancellationToken));
}
