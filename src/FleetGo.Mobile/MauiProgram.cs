using FleetGo.Mobile.Configuration;
using FleetGo.Mobile.Core.Biometrics;
using FleetGo.Mobile.Core.Otp;
using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Mobile.Services;
using FleetGo.Mobile.ViewModels;
using FleetGo.Mobile.Views;
using FleetGo.Shared.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

// Platform authenticator/autofill implementations live under Platforms/<Platform> and are
// only ever compiled into the matching target framework, so the types are only visible
// behind the matching #if - see RegisterOtpAndBiometricServices below.
#if ANDROID
using FleetGo.Mobile.Platforms.Android;
#elif IOS
using FleetGo.Mobile.Platforms.iOS;
#elif MACCATALYST
using FleetGo.Mobile.Platforms.MacCatalyst;
#endif

namespace FleetGo.Mobile;

/// <summary>
/// The app's composition root. Everything the app can resolve is registered here
/// exactly once, so there is a single place to look when asking "where does this
/// dependency come from?".
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        RegisterAppServices(builder.Services);
        RegisterAuthenticationServices(builder.Services);
        RegisterOtpAndBiometricServices(builder.Services);
        RegisterViewsAndViewModels(builder.Services);

        ConfigureOtpAutofillKeyboard();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterAppServices(IServiceCollection services)
    {
        ApiSettings apiSettings = ApiSettings.CreateDevelopmentDefaults();
        services.AddSingleton(apiSettings);

        // Injected instead of DateTimeOffset.UtcNow so time-dependent logic stays testable.
        services.AddSingleton(TimeProvider.System);

        // MAUI Essentials singletons are registered as interfaces so view models
        // depend on abstractions, not on static Current properties.
        services.AddSingleton(Connectivity.Current);
        services.AddSingleton(SecureStorage.Default);

        // BearerTokenHandler attaches the current access token (via IAccessTokenProvider,
        // registered below) to every request the typed client makes. Transient by
        // convention for HttpMessageHandlers registered with AddHttpMessageHandler.
        services.AddTransient<BearerTokenHandler>();

        // Typed client over IHttpClientFactory: the factory pools and recycles the
        // underlying handlers (a raw long-lived HttpClient misses DNS changes; a
        // new one per call exhausts sockets). It is also the seam where retry,
        // timeout and circuit-breaker policies plug in later.
        services.AddHttpClient<IFleetGoApiClient, FleetGoApiClient>(client =>
            {
                client.BaseAddress = apiSettings.BaseAddress;
                client.Timeout = apiSettings.Timeout;
            })
            .AddHttpMessageHandler<BearerTokenHandler>();
    }

    private static void RegisterAuthenticationServices(IServiceCollection services)
    {
        services.AddSingleton<ISecureTokenStore, SecureTokenStore>();

        // AuthenticationService implements both IAuthenticationService (what the UI
        // talks to) and IAccessTokenProvider (what BearerTokenHandler talks to) - the
        // same singleton instance is exposed under both interfaces so there is exactly
        // one in-memory record of the current session, not two that could drift apart.
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<IAuthenticationService>(sp => sp.GetRequiredService<AuthenticationService>());
        services.AddSingleton<IAccessTokenProvider>(sp => sp.GetRequiredService<AuthenticationService>());
    }

    private static void RegisterOtpAndBiometricServices(IServiceCollection services)
    {
        // Plain app-settings storage for the one boolean biometric opt-in flag - not a
        // secret, so Preferences is the right level of protection (see
        // BiometricPreferenceStore's own remarks).
        services.AddSingleton<IPreferences>(Preferences.Default);
        services.AddSingleton<IBiometricPreferenceStore, BiometricPreferenceStore>();

        // The coordinator is the one thing view models talk to - it is platform-agnostic
        // and fully unit-tested (see BiometricUnlockCoordinatorTests) precisely because the
        // pieces it composes (the authenticator, the preference store, session state) are
        // all behind interfaces registered here.
        services.AddSingleton<IBiometricUnlockCoordinator, BiometricUnlockCoordinator>();

        // Real biometric hardware and real SMS Retriever access only exist behind
        // platform-specific implementations - each target framework gets exactly the one
        // that matches it, never a shared/core implementation (see docs/architecture.md,
        // "Biometric unlock" and "OTP autofill").
#if ANDROID
        services.AddSingleton<IBiometricAuthenticator, AndroidBiometricAuthenticator>();
        services.AddSingleton<IOtpAutofillListener, AndroidOtpAutofillListener>();
#elif IOS
        services.AddSingleton<IBiometricAuthenticator, AppleBiometricAuthenticator>();
        // iOS has no in-app SMS access at all - the one-time-code keyboard (wired up in
        // ConfigureOtpAutofillKeyboard below) is an OS-level feature, not something this
        // listener drives, so it always reports "nothing arrived" and manual entry is what
        // actually completes the flow.
        services.AddSingleton<IOtpAutofillListener, NoOpOtpAutofillListener>();
#elif MACCATALYST
        services.AddSingleton<IBiometricAuthenticator, AppleBiometricAuthenticator>();
        services.AddSingleton<IOtpAutofillListener, NoOpOtpAutofillListener>();
#endif
    }

    /// <summary>
    /// Turns on the native one-time-code QuickType keyboard suggestion for the OTP entry
    /// field on iOS/Mac Catalyst - a pure OS-level keyboard feature, not SMS access (this
    /// app never reads SMS on iOS; see IOtpAutofillListener above). Scoped to exactly the
    /// one Entry marked StyleId="OtpCodeEntry" in OtpVerificationPage.xaml, so no other text
    /// field in the app is affected. Android does not need this - SMS Retriever autofill
    /// (AndroidOtpAutofillListener) fills the field directly instead.
    /// </summary>
    private static void ConfigureOtpAutofillKeyboard()
    {
#if IOS || MACCATALYST
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("OtpAutofillContentType", (handler, view) =>
        {
            if (view.StyleId == "OtpCodeEntry" && handler.PlatformView is UIKit.UITextField textField)
            {
                textField.TextContentType = UIKit.UITextContentType.OneTimeCode;
            }
        });
#endif
    }

    private static void RegisterViewsAndViewModels(IServiceCollection services)
    {
        // Pages and their view models are registered together: Shell resolves the
        // page from this container, and the page takes its view model by constructor.
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<HomePage>();

        // Transient rather than singleton: a fresh LoginViewModel (empty form, no stale
        // error message) every time LoginPage is navigated to, e.g. after signing out.
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginPage>();

        // Transient for the same reason: a driver should see a blank email field and no
        // stale error/cooldown state each time they arrive at this flow, not whatever was
        // left over from a previous attempt.
        services.AddTransient<OtpVerificationViewModel>();
        services.AddTransient<OtpVerificationPage>();
    }
}

