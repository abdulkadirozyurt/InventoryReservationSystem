using Serilog;

namespace InventoryService.API.Extensions;

public static class SerilogExtensions
{
    public static IHostBuilder UseInventorySerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
        });
    }


}
