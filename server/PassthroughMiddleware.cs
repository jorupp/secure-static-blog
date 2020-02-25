using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace server
{
    public class PassthroughMiddlewareSettings
    {
        public PassthroughMiddlewareHost[] Hosts { get; set; }
    }

    public class PassthroughMiddlewareHost
    {
        public string Hostname { get; set; }

        public string Path { get; set; }

    }

    public enum PassthroughType
    {
        Filesystem,
        Url,
        AzureBlobStorage,
    }

    public class PassthroughMiddleware : IMiddleware
    {
        private IOptions<PassthroughMiddlewareSettings> _settings;

        public PassthroughMiddleware(IOptions<PassthroughMiddlewareSettings> settings)
        {
            _settings = settings;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var log = context.RequestServices.GetService<ILogger<PassthroughMiddleware>>();
            log.LogInformation(context.Request.GetDisplayUrl());
            if (context.Request.GetDisplayUrl().EndsWith("/signin-oidc"))
            {
                await next(context);
                return;
            }
            var auth = await context.AuthenticateAsync();
            if (!auth.Succeeded)
            {
                await context.ChallengeAsync();
                return;
            }

            var host = _settings.Value.Hosts.FirstOrDefault(i =>
                string.Equals(i.Hostname, context.Request.Host.Value,
                    StringComparison.InvariantCultureIgnoreCase));

            if (null == host)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Host not found");
                await context.Response.CompleteAsync();
                return;
            }

            var target = new Uri(new Uri(host.Path), context.Request.Path.ToString());
            var clientFactory = context.RequestServices.GetService<IHttpClientFactory>();
            using var c = clientFactory.CreateClient();
            var res = await c.GetAsync(target);
            foreach (var h in res.Headers)
            {
                context.Response.Headers.Add(h.Key, h.Value.First());
            }
            context.Response.StatusCode = (int)res.StatusCode;
            var cs = await res.Content.ReadAsStreamAsync();
            await cs.CopyToAsync(context.Response.Body);
            //await context.Response.WriteAsync(await res.Content.ReadAsStringAsync());
            //await next();

            throw new NotImplementedException();
        }
    }
}
