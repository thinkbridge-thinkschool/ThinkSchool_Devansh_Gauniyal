# GitHub link

https://github.com/devansh-gauniyal/thinkschool/tree/day-3/task-1/day-3/task-1

# Required mentor notes/deliverables

- Project: `day-3/task-1/EntraAuthApi`
- Protected endpoint: `GET /api/protected`
- Authentication schemes: `SmartBearer`, `InternalJwt`, and `EntraId`
- Configuration keys: `InternalJwt:Issuer`, `InternalJwt:Audience`, `InternalJwt:SigningKeyBase64`, `Entra:TenantId`, `Entra:ClientId`, and `Entra:Audience`
- Restore: succeeded.
- Build: succeeded with 0 warnings and 0 errors.
- Tests: 5 passed, 0 failed, 0 skipped.
- Anonymous curl: genuine `HTTP/1.1 401 Unauthorized`, empty body, and `WWW-Authenticate: Bearer`.
- Real Microsoft Entra curl: genuine `HTTP/1.1 200 OK` with `{"message":"Authentication succeeded."}`.

# Program.cs authentication setup

```csharp
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(InternalJwtOptions.SectionName)
        .Get<InternalJwtOptions>() ?? new InternalJwtOptions();
    options.ValidateAndGetSigningKey();
    return options;
});
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var options = configuration
        .GetSection(EntraOptions.SectionName)
        .Get<EntraOptions>() ?? new EntraOptions();
    options.ValidateAndGetAuthority();
    return options;
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = AuthenticationSchemes.SmartBearer;
        options.DefaultChallengeScheme = AuthenticationSchemes.SmartBearer;
    })
    .AddPolicyScheme(
        AuthenticationSchemes.SmartBearer,
        displayName: null,
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var entraOptions = context.RequestServices
                    .GetRequiredService<EntraOptions>();
                var entraAuthority = entraOptions.ValidateAndGetAuthority();
                var authorization = context.Request.Headers.Authorization.ToString();
                const string bearerPrefix = "Bearer ";

                if (authorization.StartsWith(
                        bearerPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var token = authorization[bearerPrefix.Length..].Trim();
                    var handler = new JwtSecurityTokenHandler();

                    if (handler.CanReadToken(token))
                    {
                        var issuer = handler.ReadJwtToken(token).Issuer;
                        if (string.Equals(
                                issuer,
                                entraAuthority,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return AuthenticationSchemes.EntraId;
                        }
                    }
                }

                return AuthenticationSchemes.InternalJwt;
            };
        })
    .AddJwtBearer(AuthenticationSchemes.InternalJwt)
    .AddJwtBearer(AuthenticationSchemes.EntraId);

builder.Services
    .AddOptions<JwtBearerOptions>(AuthenticationSchemes.InternalJwt)
    .Configure<InternalJwtOptions>((options, internalJwt) =>
    {
        var internalSigningKey = internalJwt.ValidateAndGetSigningKey();
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = internalJwt.Issuer,
            ValidateAudience = true,
            ValidAudience = internalJwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(internalSigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    });

builder.Services
    .AddOptions<JwtBearerOptions>(AuthenticationSchemes.EntraId)
    .Configure<EntraOptions>((options, entra) =>
    {
        var entraAuthority = entra.ValidateAndGetAuthority();
        options.Authority = entraAuthority;
        options.Audience = entra.Audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = entra.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    });
```

# Successful curl command and sanitized genuine response

```bash
curl -i \
  -H "Authorization: Bearer $ENTRA_ACCESS_TOKEN" \
  http://127.0.0.1:5283/api/protected
```

Genuine status: `HTTP/1.1 200 OK`

Genuine response body:

```json
{"message":"Authentication succeeded."}
```

# What did you learn this session?

I learned that one API can use a policy scheme to route tokens to different JWT handlers based on the issuer. Reading `iss` only chooses the handler; that handler must still validate the token's signature, issuer, audience, and expiry.

# What would break this?

A wrong tenant, issuer, or audience, an expired token, an invalid signature, missing API permission consent, or routing an issuer to the wrong scheme will cause authentication to fail.
