using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();
        services.AddSingleton<TokenCredential>(
            _ => new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()));
    })
    .Build();

host.Run();
