# PTDoc Communication Delivery

PTDoc uses Azure Communication Services (ACS) as the only outbound email and SMS provider. Application code calls `ICommunicationService`; UI components and API endpoints must not reference ACS SDK types directly. Password reset links are backed by hashed, single-use reset tokens and are completed through `POST /api/communications/password-reset/complete`.

## Required Production Configuration

Set these values through environment variables, user-secrets for local development, or Azure Key Vault references in production:

```text
Communication__PublicBaseUrl=https://app.ptdoc.com
Communication__RecipientHashSalt=<random high-entropy secret>
Communication__Azure__ConnectionString=<acs connection string>
Communication__Azure__EmailFromAddress=no-reply@ptdoc.com
Communication__Azure__SmsFromPhoneNumber=+15550100000
```

Startup outside Development and Testing fails when the recipient hash salt, ACS connection string, sender email, or SMS number is missing. Development and Testing use null senders by default.

## ACS Email Setup

1. Create or select an Azure Communication Services resource.
2. Configure Email for the resource and connect a verified email domain.
3. Verify the sender address used by `Communication:Azure:EmailFromAddress`.
4. Store the ACS connection string in Key Vault or an environment variable; do not commit it to appsettings.

## ACS SMS Setup

1. In the ACS resource, acquire or connect an SMS-enabled phone number for the deployment region.
2. Use that number for `Communication:Azure:SmsFromPhoneNumber`.
3. Confirm the number supports the target geography and message type before enabling production sends.

## Azure Key Vault Pattern

For hosted environments, store the following secrets in Key Vault and expose them to the API through app configuration or managed identity:

```text
Communication--RecipientHashSalt
Communication--Azure--ConnectionString
Communication--Azure--EmailFromAddress
Communication--Azure--SmsFromPhoneNumber
```

Keep templates generic. Do not include diagnosis, treatment detail, insurance data, DOB, note content, reset tokens outside links, or patient names in SMS.
