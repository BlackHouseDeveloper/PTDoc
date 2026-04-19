using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.AI;
using PTDoc.Application.Auth;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Intake;
using PTDoc.Application.LocalData;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Security;
using PTDoc.Application.Services;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.LocalData;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
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

		builder.Services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());
		builder.Services.AddScoped<AuthenticationStateProvider, MauiAuthenticationStateProvider>();
		builder.Services.AddScoped<ITokenStore, SecureStorageTokenStore>();
		// ITokenService is registered via typed HttpClient below
		builder.Services.AddScoped<IUserService, MauiUserService>();
		builder.Services.AddScoped<AuthenticatedHttpMessageHandler>();
		builder.Services.AddScoped<IThemeService, MauiThemeService>();
		builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
		builder.Services.AddSingleton<LocalSyncCoordinator>();
		builder.Services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<LocalSyncCoordinator>());
		builder.Services.AddScoped<IIntakeService, IntakeApiService>();
		builder.Services.AddScoped<IIntakeInviteService, HttpIntakeInviteService>();
		builder.Services.AddScoped<IIntakeDeliveryService, IntakeDeliveryApiService>();
		builder.Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
		builder.Services.AddSingleton<IOutcomeMeasureRegistry, OutcomeMeasureRegistry>();
		builder.Services.AddScoped<IAppointmentService, AppointmentApiService>();
		builder.Services.AddScoped<INoteWorkspaceService, NoteWorkspaceApiService>();
		builder.Services.AddScoped<INoteDraftLocalPersistenceService, MauiNoteDraftLocalPersistenceService>();
		builder.Services.AddTransient<DraftAutosaveService>();
		builder.Services.AddScoped<IAdminApprovalService, AdminApprovalApiService>();
		builder.Services.AddScoped<INotificationCenterService, HttpNotificationCenterService>();
		builder.Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
		builder.Services.AddScoped<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
		builder.Services.AddScoped<IHeaderConfigurationService, HeaderConfigurationService>();

		// Register HTTP-backed AI generation for the shared UI workspace.
		// Uses the authenticated ApiClient so generated requests carry the bearer token.
		builder.Services.AddScoped<IAiClinicalGenerationService>(sp =>
		{
			var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient");
			return new HttpAiClinicalGenerationService(httpClient);
		});

		// ----------------------------------------------------------------
		// Local encrypted SQLite database (Sprint D)
		// IDbKeyProvider uses platform SecureStorage (iOS Keychain / Android Keystore)
		// to generate and retrieve the per-device SQLCipher encryption key.
		//
		// The SqliteConnection is registered as a Singleton because:
		//   • It holds the SQLCipher-authenticated open connection state.
		//   • Closing and reopening it would require rebuilding the encrypted connection.
		//
		// LocalDbContext is registered as Scoped so each DI scope (UI component,
		// background task) gets its own EF Core context instance.  EF Core does not
		// dispose connections it does not own, so the shared Singleton connection
		// remains open across all context lifetimes.
		// ----------------------------------------------------------------
		SqliteProviderBootstrapper.EnsureInitialized();
		builder.Services.AddSingleton<IDbKeyProvider, SecureStorageDbKeyProvider>();

		builder.Services.AddSingleton<SqliteConnection>(sp =>
		{
			var keyProvider = sp.GetRequiredService<IDbKeyProvider>();
			// Use Task.Run to avoid a SynchronizationContext deadlock when the DI factory
			// is invoked on the MAUI main thread.  SecureStorage.GetAsync marshals back to
			// the platform dispatcher, which can deadlock with a direct .GetAwaiter().GetResult().
			var key = Task.Run(() => keyProvider.GetKeyAsync()).GetAwaiter().GetResult();

			var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ptdoc_local.db");

			// Open the encrypted connection before EF Core sees it so the
			// SQLCipher password is applied at connection-open time.
			var connectionString = new SqliteConnectionStringBuilder
			{
				DataSource = dbPath,
				Password = key
			}.ToString();
			var connection = new SqliteConnection(connectionString);
			connection.Open();

			return connection;
		});

		// LocalDbContext is Scoped — each scope gets its own context instance (thread-safe).
		// All instances share the Singleton SqliteConnection, which keeps SQLCipher auth alive.
		builder.Services.AddDbContext<LocalDbContext>((sp, options) =>
		{
			var connection = sp.GetRequiredService<SqliteConnection>();
			options.UseSqlite(connection);
		}, ServiceLifetime.Scoped);

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

		// ----------------------------------------------------------------
		// Sprint H — Offline Sync Orchestrator
		// LocalSyncOrchestrator uses a typed HttpClient so that it
		// automatically carries the authenticated session token (via
		// AuthenticatedHttpMessageHandler) and the correct API base URL.
		// ----------------------------------------------------------------
		builder.Services.AddHttpClient<ILocalSyncOrchestrator, LocalSyncOrchestrator>(client =>
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
