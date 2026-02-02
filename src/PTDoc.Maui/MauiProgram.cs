using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;
using PTDoc.Application.Auth;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Services;
using PTDoc.Maui.Auth;

namespace PTDoc;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		builder.Services.AddAuthorizationCore();
		builder.Services.AddScoped<AuthenticationStateProvider, MauiAuthenticationStateProvider>();
		builder.Services.AddScoped<ITokenStore, SecureStorageTokenStore>();
		// ITokenService is registered via typed HttpClient below
		builder.Services.AddScoped<IUserService, MauiUserService>();
		builder.Services.AddScoped<AuthenticatedHttpMessageHandler>();
		builder.Services.AddScoped<IThemeService, ThemeService>();
		
		// Register App as transient to inject services into constructor
		builder.Services.AddTransient<App>();

		// Use 10.0.2.2 for Android emulator to reach host machine's localhost
		// For iOS simulator, localhost works fine
#if ANDROID
		var apiBaseUrl = "http://10.0.2.2:5170";
#else
		var apiBaseUrl = "http://localhost:5170";
#endif

		builder.Services.AddHttpClient<ITokenService, TokenService>(client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
		});

		builder.Services.AddHttpClient("ApiClient", client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
		})
		.AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

		builder.Services.AddScoped(sp =>
			sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
