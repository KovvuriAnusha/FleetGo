using System.Collections.Concurrent;
using FleetGo.API.Auth;

namespace FleetGo.API.Tests;

/// <summary>
/// Test-only <see cref="IOtpSender"/>: records what would have been sent instead of
/// logging it, so a test can read back the exact code <see cref="OtpService"/> generated
/// without scraping log output or (worse) needing a real SMS provider. Registered in place
/// of <see cref="DevelopmentOtpSender"/> by <see cref="FleetGoApiFactory"/>.
/// </summary>
public sealed class RecordingOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<string, string> _lastCodeByPhoneNumber = new();
    private int _sendCount;

    /// <summary>How many codes have been sent across every phone number, for asserting "no code was sent".</summary>
    public int SendCount => _sendCount;

    public Task SendAsync(string phoneNumber, string code, string purpose, CancellationToken cancellationToken)
    {
        _lastCodeByPhoneNumber[phoneNumber] = code;
        Interlocked.Increment(ref _sendCount);
        return Task.CompletedTask;
    }

    /// <summary>The most recent code sent to this phone number, or <see langword="null"/> if none was ever sent.</summary>
    public string? LastCodeSentTo(string phoneNumber) => _lastCodeByPhoneNumber.GetValueOrDefault(phoneNumber);
}
