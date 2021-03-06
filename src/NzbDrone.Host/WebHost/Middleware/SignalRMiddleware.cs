using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Configuration;
using NzbDrone.SignalR;

namespace Radarr.Host.Middleware
{
    public class SignalRMiddleware : IAspNetCoreMiddleware
    {
        private readonly IContainer _container;
        private readonly Logger _logger;
        private static string API_KEY;
        private static string URL_BASE;
        public int Order => 1;

        public SignalRMiddleware(IContainer container,
                                 IConfigFileProvider configFileProvider,
                                 Logger logger)
        {
            _container = container;
            _logger = logger;
            API_KEY = configFileProvider.ApiKey;
            URL_BASE = configFileProvider.UrlBase;
        }

        public void Attach(IApplicationBuilder appBuilder)
        {
            appBuilder.UseWebSockets();

            appBuilder.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/signalr") &&
                    !context.Request.Path.Value.EndsWith("/negotiate"))
                {
                    if (!context.Request.Query.ContainsKey("access_token") ||
                        context.Request.Query["access_token"] != API_KEY)
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Unauthorized");
                        return;
                    }
                }

                try
                {
                    await next();
                }
                catch (OperationCanceledException e)
                {
                    // Demote the exception to trace logging so users don't worry (as much).
                    _logger.Trace(e);
                }
            });

#if NETCOREAPP
            appBuilder.UseEndpoints(x =>
            {
                x.MapHub<MessageHub>(URL_BASE + "/signalr/messages");
            });
#else
            appBuilder.UseSignalR(x =>
            {
                x.MapHub<MessageHub>("/signalr/messages");
            });
#endif

            // This is a side effect of haing multiple IoC containers, TinyIoC and whatever
            // Kestrel/SignalR is using. Ideally we'd have one IoC container, but that's non-trivial with TinyIoC
            // TODO: Use a single IoC container if supported for TinyIoC or if we switch to another system (ie Autofac).
            var hubContext = appBuilder.ApplicationServices.GetService<IHubContext<MessageHub>>();
            _container.Register(hubContext);
        }
    }
}
