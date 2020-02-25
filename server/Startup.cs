using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(c => c.AddConsole());
            services.AddHttpClient();

            services.AddAuthentication(AzureADDefaults.AuthenticationScheme)
                .AddAzureAD(options => Configuration.Bind("AzureAd", options));

            //services.AddRazorPages().AddMvcOptions(options =>
            //{
            //    var policy = new AuthorizationPolicyBuilder()
            //        .RequireAuthenticatedUser()
            //        .Build();
            //    options.Filters.Add(new AuthorizeFilter(policy));
            //});
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            //app.UseStaticFiles();

            //app.UseRouting();

            app.Use(async (context, next) =>
            {
                var log = context.RequestServices.GetService<ILogger<Program>>();
                log.LogInformation(context.Request.GetDisplayUrl());
                if (context.Request.GetDisplayUrl().EndsWith("/signin-oidc"))
                {
                    await next();
                    return;
                }
                var auth = await context.AuthenticateAsync();
                if (!auth.Succeeded)
                {
                    await context.ChallengeAsync();
                    return;
                }
                var target = new Uri(new Uri("http://localhost:9000/"), context.Request.Path.ToString());
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
            });

            app.UseAuthentication();
            app.UseAuthorization();

            //app.UseEndpoints(endpoints =>
            //{
            //    endpoints.MapRazorPages();
            //    endpoints.MapControllers();
            //});
        }
    }
}
