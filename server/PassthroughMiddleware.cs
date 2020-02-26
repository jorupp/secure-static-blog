using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
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

        public PassthroughType Type { get; set; }

        public string ConnectionStringName { get; set; }

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
        private readonly IOptions<PassthroughMiddlewareSettings> _settings;
        private readonly IConfiguration _configuration;

        public PassthroughMiddleware(IOptions<PassthroughMiddlewareSettings> settings, IConfiguration configuration)
        {
            _settings = settings;
            _configuration = configuration;
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

            log.LogInformation($"searching for {context.Request.Host.Value} in {string.Join(", ", _settings.Value.Hosts.Select(i => i.Hostname))}");

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

            switch (host.Type)
            {
                case PassthroughType.Url:
                    var target = new Uri(new Uri(host.Path), context.Request.Path.ToString());
                    var clientFactory = context.RequestServices.GetService<IHttpClientFactory>();
                    using (var c = clientFactory.CreateClient())
                    {
                        var res = await c.GetAsync(target);
                        foreach (var h in res.Headers)
                        {
                            context.Response.Headers.Add(h.Key, h.Value.First());
                        }
                        context.Response.StatusCode = (int)res.StatusCode;
                        var cs = await res.Content.ReadAsStreamAsync();
                        await cs.CopyToAsync(context.Response.Body);
                        //await context.Response.WriteAsync(await res.Content.ReadAsStringAsync());
                        return;
                    }
                case PassthroughType.AzureBlobStorage:
                    var acc = CloudStorageAccount.Parse(_configuration.GetConnectionString(host.ConnectionStringName));
                    var cl = acc.CreateCloudBlobClient();
                    var container = cl.GetContainerReference(host.Path);
                    log.LogInformation($"container uri: {container.Uri}");
                    log.LogInformation($"path: {context.Request.Path.ToString()}");
                    var blobPath = new Uri(container.Uri.ToString() + context.Request.Path.ToString());
                    if (blobPath.ToString().EndsWith("/"))
                    {
                        blobPath = new Uri(blobPath, "index.html");
                    }
                    log.LogInformation($"blob path: {blobPath}");
                    var blob = await cl.GetBlobReferenceFromServerAsync(blobPath);
                    await blob.DownloadRangeToStreamAsync(context.Response.Body, null, null);
                    return;
            }

            await next(context);
        }
    }
}
