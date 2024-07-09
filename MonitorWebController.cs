using Microsoft.AspNetCore;

namespace Tractus.Ndi.Monitor;
public class MonitorWebController
{
    private IWebHost? Host { get; set; }

    public MonitorWebController()
    {
    }

    public async Task Rebuild()
    {
        if (this.Host is not null)
        {
            await this.Host.StopAsync();
            this.Host.Dispose();
            this.Host = null;
        }

        var host = WebHost.CreateDefaultBuilder(Array.Empty<string>())
            .ConfigureServices(s => s.AddCors(o =>
            {
                o.AddDefaultPolicy(b =>
                {
                    b.AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .SetIsOriginAllowed(_ => true);
                });

            }).AddSignalR())
            .UseUrls($"http://*:{8903}")
            .Configure((Action<IApplicationBuilder>)(w =>
            {
                var fsOptions = new FileServerOptions
                {

                };

                fsOptions.StaticFileOptions.OnPrepareResponse = (context) =>
                {
                    context.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
                    context.Context.Response.Headers.Append("Expires", "-1");
                };

                w.UseCors();
                w.UseFileServer(fsOptions);
                //w.UseDefaultFiles();
                //w.UseStaticFiles();

                w.UseRouting();


                w.UseEndpoints((Action<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder>)(e =>
                {
                    e.MapGet("/source/{sourceName}", (string sourceName) =>
                    {
                        Program.Display.RequestUpdateNdiSource(sourceName);
                    });
                }));
            }));

        host = host.ConfigureServices(s =>
        {

        });

        this.Host = host.Build();
        this.Host.RunAsync();
    }
}
