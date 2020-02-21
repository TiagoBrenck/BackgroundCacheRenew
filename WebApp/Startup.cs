using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Repository;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using Microsoft.Identity.Web.UI;

namespace WebApp
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
            services.AddDbContext<CacheDbContext>(options => options.UseSqlServer(Configuration.GetConnectionString("TokenCacheDbConnStr")));
            services.AddScoped<IMsalAccountActivityRepository, MsalAccountActivityRepository>();

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
                // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
                options.HandleSameSiteCookieCompatibility();
            });

            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddSignIn("AzureAD", Configuration, options =>
                {
                    Configuration.Bind("AzureAD", options);
                    options.Events.OnAuthorizationCodeReceived = async context =>
                    {
                        var tokenAcquisition = context.HttpContext.RequestServices.GetRequiredService<ITokenAcquisition>();
                        var app = tokenAcquisition.GetOrBuildConfidentialClientApplication();

                        var account = (await app.GetAccountsAsync())
                            .Where(x => x.Username == context.HttpContext.User.GetLoginHint())
                            .FirstOrDefault();

                        var accountActivity = new MsalAccountActivity(account, context.HttpContext.User.GetMsalAccountId());

                        var repo = context.HttpContext.RequestServices.GetRequiredService<IMsalAccountActivityRepository>();
                        await repo.UpsertActivity(accountActivity);
                    };
                });

            // Token acquisition service based on MSAL.NET
            // and chosen token cache implementation
            services.AddWebAppCallsProtectedWebApi(Configuration, new string[] { Constants.ScopeUserRead })
                .AddDistributedTokenCaches();

            services.AddDistributedSqlServerCache(options =>
            {
                options.ConnectionString = Configuration.GetConnectionString("TokenCacheDbConnStr");
                options.SchemaName = "dbo";
                options.TableName = "TokenCache";
            });

            services.AddControllersWithViews(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            }).AddMicrosoftIdentityUI();

            services.AddRazorPages();
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
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapRazorPages();
            });
        }
    }
}
