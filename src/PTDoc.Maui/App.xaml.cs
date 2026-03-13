namespace PTDoc;

using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Application.LocalData;

public partial class App : Microsoft.Maui.Controls.Application
{
	public App(ITokenStore tokenStore, ILocalDbInitializer localDbInitializer, ILogger<App> logger)
	{
		InitializeComponent();

		MainPage = new MainPage();

		// Initialise local encrypted SQLite database before any data access
		InitializeLocalDatabaseAsync(localDbInitializer, logger);

		// Enterprise security: Validate token on app startup
		ValidateTokenOnStartup(tokenStore, logger);
	}
	
	private async void InitializeLocalDatabaseAsync(ILocalDbInitializer localDbInitializer, ILogger<App> logger)
	{
		try
		{
			await localDbInitializer.InitializeAsync();
		}
		catch (Exception ex)
		{
			// Log but do not crash — offline features will be unavailable
			logger.LogError(ex, "Failed to initialise local database; offline features may be unavailable");
		}
	}

	private async void ValidateTokenOnStartup(ITokenStore tokenStore, ILogger<App> logger)
	{
		try
		{
			logger.LogInformation("Validating stored authentication tokens on app startup");
			var tokens = await tokenStore.GetAsync();
			
			if (tokens is not null)
			{
				// Check if token is expired
				if (tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow)
				{
					logger.LogWarning("Stored token expired on {ExpiredAt}, clearing for security", tokens.ExpiresAtUtc);
					await tokenStore.ClearAsync();
				}
				else
				{
					logger.LogInformation("Token valid until {ExpiresAt}", tokens.ExpiresAtUtc);
				}
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error validating token on startup, clearing for security");
			try
			{
				await tokenStore.ClearAsync();
			}
			catch
			{
				// Ignore errors during cleanup
			}
		}
	}
}