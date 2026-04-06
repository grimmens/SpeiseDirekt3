namespace SpeiseDirekt.Api.Dtos;

public record CreateStripePaymentDto
{
    public string SuccessUrl { get; init; } = string.Empty;
    public string CancelUrl { get; init; } = string.Empty;
}

public record RefundPaymentDto
{
    public decimal? Amount { get; init; }
    public string? Reason { get; init; }
}
