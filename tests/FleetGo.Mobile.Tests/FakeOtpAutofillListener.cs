using FleetGo.Mobile.Core.Otp;

namespace FleetGo.Mobile.Tests;

/// <summary>Test double for <see cref="IOtpAutofillListener"/> - resolves immediately with a fixed code (or none), instead of waiting on any platform mechanism.</summary>
internal sealed class FakeOtpAutofillListener : IOtpAutofillListener
{
    private readonly string? _code;

    public FakeOtpAutofillListener(string? code) => _code = code;

    public Task<string?> ListenForCodeAsync(CancellationToken cancellationToken) => Task.FromResult(_code);
}
