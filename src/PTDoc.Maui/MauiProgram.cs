using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Auth;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Intake;
using PTDoc.Application.LocalData;
using PTDoc.Application.Security;
using PTDoc.Application.Services;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.LocalData;
using PTDoc.Infrastructure.Services;
using PTDoc.UI.Services;
using PTDoc.Maui.Auth;
using PTDoc.Maui.Security;
using PTDoc.Maui.Services;

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
		builder.Services.AddScoped<IThemeService, MauiThemeService>();
		builder.Services.AddScoped<ISyncService, SyncService>();
		builder.Services.AddScoped<IConnectivityService, ConnectivityService>();
		builder.Services.AddScoped<IIntakeService, MockIntakeService>();
		builder.Services.AddScoped<IIntakeInviteService, MockIntakeInviteService>();
		builder.Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
		builder.Services.AddScoped<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
		builder.Services.AddScoped<IHeaderConfigurationService, HeaderConfigurationService>();

		// ----------------------------------------------------------------
		// Local encrypted SQLite database (Sprint D)
		// IDbKeyProvider uses platform SecureStorage (iOS Keychain / Android Keystore)
		// to generate and retrieve the per-device SQLCipher encryption key.
		// ----------------------------------------------------------------
		builder.Services.AddSingleton<IDbKeyProvider, SecureStorageDbKeyProvider>();

		builder.Services.AddDbContext<LocalDbContext>((sp, options) =>
		{
			var keyProvider = sp.GetRequiredService<IDbKeyProvider>();
			// Use Task.Run to avoid a SynchronizationContext deadlock when the DI factory
			// is invoked on the MAUI main thread.  SecureStorage.GetAsync marshals back to
			// the platform dispatcher, which can deadlock with a direct .GetAwaiter().GetResult().
			var key = Task.Run(() => keyProvider.GetKeyAsync()).GetAwaiter().GetResult();

			var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ptdoc_local.db");

			// Open the connection manually so we can run PRAGMA key before EF Core
			// sees the connection.  This is required for SQLCipher encryption.
			var connection = new SqliteConnection($"Data Source={dbPath}");
			connection.Open();

			using (var command = connection.CreateCommand())
			{
				command.CommandText = "PRAGMA key = $key;";
				var param = command.CreateParameter();
				param.ParameterName = "$key";
				param.Value = key;
				command.Parameters.Add(param);
				command.ExecuteNonQuery();
			}

			options.UseSqlite(connection);
		}, ServiceLifetime.Singleton);

		// Generic local repository for all ILocalEntity types
		builder.Services.AddScoped(typeof(ILocalRepository<>), typeof(LocalRepository<>));

		// Initializer — called at startup to ensure the schema exists
		builder.Services.AddSingleton<ILocalDbInitializer, LocalDbInitializer>();
		
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
