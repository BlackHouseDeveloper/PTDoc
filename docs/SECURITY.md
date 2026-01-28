# PTDoc Enterprise Security Configuration

## Overview
PTDoc implements enterprise-grade security measures suitable for HIPAA-compliant healthcare applications. This document outlines the authentication and session management policies.

## Security Policies

### Web Application (Cookie Authentication)

#### Session Timeouts
- **Inactivity Timeout**: 15 minutes
- **Absolute Session Expiration**: 8 hours maximum
- **Sliding Expiration**: Enabled (session extends on activity)

#### Cookie Security
- **HttpOnly**: Enabled (prevents JavaScript access)
- **Secure**: Always (requires HTTPS in production)
- **SameSite**: Strict (CSRF protection)

#### Configuration Location
`src/PTDoc.Web/Program.cs` - Cookie authentication options

```csharp
options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
options.SlidingExpiration = true;
options.Cookie.MaxAge = TimeSpan.FromHours(8);
```

### Mobile Application (JWT Authentication)

#### Token Lifetimes
- **Access Token**: 15 minutes
- **Refresh Token**: 7 days
- **Token Validation**: On every app startup

#### Token Security
- **Storage**: SecureStorage (encrypted at OS level)
- **Validation**: Automatic expiration check and refresh
- **Cleanup**: Expired tokens automatically cleared on startup

#### Configuration Location
`src/PTDoc.Api/Auth/JwtOptions.cs` and `appsettings.json`

```json
{
  "Jwt": {
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  }
}
```

## HIPAA Compliance Considerations

### Automatic Session Termination
- Web sessions automatically terminate after 15 minutes of inactivity
- Mobile tokens expire after 15 minutes (with automatic refresh)
- Absolute maximum session time: 8 hours (web) / 7 days (mobile with active use)

### Secure Logout
- Logout button available in all authenticated pages
- Logout clears all authentication data (cookies/tokens)
- Force reload ensures clean authentication state

### Token Validation
- Mobile app validates tokens on startup
- Expired tokens automatically cleared
- Failed refresh attempts trigger re-authentication

## User Experience

### Web Application
1. **Login**: User authenticates with PIN/username
2. **Active Session**: Session extends with each action (up to 8 hours)
3. **Inactivity**: Session expires after 15 minutes of no activity
4. **Logout**: User can manually logout anytime
5. **Re-authentication**: Required after session expiration

### Mobile Application
1. **Login**: User authenticates with PIN/username
2. **Token Storage**: Access token (15min) and refresh token (7 days) stored securely
3. **Automatic Refresh**: Access token auto-refreshes when needed
4. **App Restart**: Validates tokens, clears if expired
5. **Logout**: Clears all stored tokens
6. **Re-authentication**: Required after refresh token expiration or explicit logout

## Testing Security Policies

### Web Application Testing
```bash
# Test cookie expiration
1. Login to web application
2. Wait 15 minutes without activity
3. Try to access protected page - should redirect to login

# Test absolute expiration
1. Login and remain active
2. After 8 hours, should be logged out regardless of activity

# Test logout
1. Click logout button
2. Verify redirect to login page
3. Use browser back button - should require re-authentication
```

### Mobile Application Testing
```bash
# Test token expiration
1. Login to mobile app
2. Close app
3. Wait for access token to expire (15+ minutes)
4. Reopen app - should automatically refresh or require login

# Test app restart validation
1. Login to mobile app
2. Manually advance device time by 8+ days
3. Restart app - should clear tokens and require login

# Test logout
1. Click logout button
2. Verify redirect to login page
3. Verify tokens cleared from SecureStorage
```

## Configuration Override

### Development Environment
For development/testing, you may need longer session times:

**Web** - `appsettings.Development.json`:
```json
{
  "CookieAuth": {
    "ExpireMinutes": 60,
    "MaxAgeHours": 24
  }
}
```

**Mobile** - `appsettings.Development.json`:
```json
{
  "Jwt": {
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 30
  }
}
```

### Production Environment
Production should use stricter timeouts as configured in the code defaults.

## Audit and Compliance

### Logging
All authentication events are logged:
- User login attempts (success/failure)
- Token refresh operations
- Session expirations
- Logout events
- Token validation failures

### Monitoring
Implement monitoring for:
- Failed login attempts (potential security breach)
- Token refresh failures (system issues)
- Unusual session patterns (security anomaly)

## Security Best Practices

1. **Never disable HTTPS** in production
2. **Rotate JWT signing keys** periodically
3. **Monitor authentication logs** for suspicious activity
4. **Implement rate limiting** on login endpoints
5. **Use strong PIN policies** (minimum length, complexity)
6. **Enable MFA** for administrative access
7. **Regular security audits** of authentication flow
8. **HIPAA audit trail** for all patient data access

## Troubleshooting

### "Session expired" messages
- Normal behavior after 15 minutes of inactivity
- Users should re-authenticate
- Educate users about timeout policy

### Tokens persist after logout
- Clear browser cookies (web)
- Uninstall/reinstall app (mobile - extreme case)
- Check SecureStorage implementation

### Automatic logout issues
- Verify system time is correct
- Check token expiration configuration
- Review authentication logs

## Support

For security concerns or questions:
1. Review this documentation
2. Check authentication logs
3. Contact development team
4. For security vulnerabilities, use private disclosure
