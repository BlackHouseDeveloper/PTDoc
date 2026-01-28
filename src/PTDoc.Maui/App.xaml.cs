namespace PTDoc;

using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;

public partial class App : Microsoft.Maui.Controls.Application
{
	public App(ITokenStore tokenStore, ILogger<App> logger)
	{
		InitializeComponent();

		MainPage = new MainPage();
		
		// Enterprise security: Validate token on app startup
		ValidateTokenOnStartup(tokenStore, logger);
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