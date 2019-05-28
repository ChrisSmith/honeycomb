using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Honeycomb;
using Honeycomb.AspNetCore.Hosting;
using Honeycomb.Models;
using Honeycomb.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCensus.Tags;
using OpenCensus.Trace;
using OpenCensus.Trace.Propagation;
using OpenCensus.Trace.Sampler;

namespace TracingSample
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
            services.Configure<HoneycombApiSettings>(o => Configuration.GetSection("HoneycombSettings").Bind(o));
            services.TryAddSingleton<IHoneycombService, HoneycombService>();
            services.AddSingleton<HoneycombExportHandler>(sp => 
                new HoneycombExportHandler(
                    sp.GetRequiredService<IHoneycombService>(), 
                    "Test", 
                    sp.GetRequiredService<IOptions<HoneycombApiSettings>>()));
            services.AddHttpClient("honeycomb");
            services.AddHttpContextAccessor();
            services.AddHostedService<HoneycombBackgroundService>();

            services.AddSingleton<ITracer>(sp => 
            {
                var handler = sp.GetRequiredService(typeof(HoneycombExportHandler)) as HoneycombExportHandler;
                Tracing.ExportComponent.SpanExporter.RegisterHandler("Honeycomb", handler);
                return Tracing.Tracer;
            });
            services.AddSingleton<ISampler>(Samplers.AlwaysSample);
            services.AddSingleton<IPropagationComponent>(new DefaultPropagationComponent());

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseMiddleware<HoneycombTracerMiddleware>();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }



    public class HoneycombTracerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HoneycombTracerMiddleware> _logger;
        private readonly ITracer _tracer;
        private readonly ISampler _sampler;
        public HoneycombTracerMiddleware(RequestDelegate next,
            ITracer tracer,

            ILogger<HoneycombTracerMiddleware> logger, ISampler sampler)
        {
            _sampler = sampler;
            _next = next;
            _tracer = tracer;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            using (var scope = _tracer
                .SpanBuilder($"{context.GetRouteValue("controller")}#{context.GetRouteValue("action")}")
                .SetSampler(_sampler)
                .StartScopedSpan(out var span))
            {
                span.PutAttribute("request.path", context.Request.Path.Value);
                span.PutAttribute("request.method", context.Request.Method);
                span.PutAttribute("request.http_version", context.Request.Protocol);
                span.PutAttribute("request.content_length", context.Request.ContentLength.GetValueOrDefault());
                span.PutAttribute("request.header.x_forwarded_proto", context.Request.Scheme);
                span.PutAttribute("meta.local_hostname", Environment.MachineName);

                try
                {
                    await _next.Invoke(context);

                    span.PutAttribute("name", $"{context.GetRouteValue("controller")}#{context.GetRouteValue("action")}");
                    span.PutAttribute("action", context.GetRouteValue("action").ToString());
                    span.PutAttribute("controller", context.GetRouteValue("controller").ToString());
                    span.PutAttribute("response.content_length", context.Response.ContentLength.GetValueOrDefault());
                    span.PutAttribute("response.status_code", context.Response.StatusCode);
                }
                catch (Exception ex)
                {
                    span.PutAttribute("request.error", ex.Source);
                    span.PutAttribute("request.error_detail", ex.Message);
                    throw;
                }
            }
        }
    }
    public static class SpanExtensions
    {
        public static void PutAttribute(this ISpan span, string key, string value)
        {
            span.PutAttribute(key, AttributeValue.StringAttributeValue(value));
        }

        public static void PutAttribute(this ISpan span, string key, long value)
        {
            span.PutAttribute(key, AttributeValue.LongAttributeValue(value));
        }
    }
}
