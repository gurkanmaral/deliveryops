namespace DeliveryOps.PosBridge.Core.Abstractions;

/// <summary>
/// Seam for the card terminal. A NarPOS adapter can implement this once its integration interface is
/// available; until then <see cref="Services.ManualPaymentTerminal"/> asks the cashier to confirm.
/// </summary>
public interface IPaymentTerminal
{
    string Name { get; }

    /// <summary>True when the terminal reports the result itself; false when the cashier must confirm it.</summary>
    bool ReportsResultAutomatically { get; }

    Task<PaymentTerminalResult> ChargeAsync(decimal amount, string orderReference, CancellationToken cancellationToken);
}

public sealed record PaymentTerminalResult(bool Approved, string? Reference, string? Error)
{
    public static PaymentTerminalResult ApprovedWith(string? reference) => new(true, reference, null);
    public static PaymentTerminalResult Declined(string error) => new(false, null, error);
}
