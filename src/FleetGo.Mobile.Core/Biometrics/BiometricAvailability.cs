namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>Whether biometric authentication can be offered on this device right now.</summary>
public enum BiometricAvailability
{
    /// <summary>Hardware present, enrolled, and ready to use.</summary>
    Available,

    /// <summary>Hardware present, but the user has not enrolled a fingerprint/face.</summary>
    NotEnrolled,

    /// <summary>No biometric hardware on this device, or the OS is too old to support it.</summary>
    NotSupported,

    /// <summary>Temporarily unusable - typically a lockout after too many failed system-level attempts.</summary>
    DeniedOrLockedOut,

    /// <summary>The platform reported a state this abstraction does not have a specific case for.</summary>
    Unknown,
}
