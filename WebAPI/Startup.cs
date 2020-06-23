// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using Microsoft.Identity.Web.TokenCacheProviders.InMemory;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace WebAPI
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

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddProtectedWebApi(Configuration);

            services.AddProtectedWebApiCallsProtectedWebApi(Configuration)
                    .AddDistributedTokenCaches();

            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => 
            {
                Configuration.Bind("AzureAd", options);
                var existingHandler = options.Events.OnTokenValidated; // handler from Microsoft.Identity.Web

                options.Events.OnTokenValidated = async context =>
                {
                    // Execute Microsoft.Identity.Web event handler first
                    await existingHandler(context).ConfigureAwait(false);

                    // Since OnTokenValidated will be called multiple times, it is good to filter what operation will trigger the insert of 
                    // MsalAccountActivity, otherwise the DB could get overwhelmed and the app gets slow
                    if (context.HttpContext.Request.Method == "POST" && context.Request.Path == "/api/todolist")
                    {
                        var tokenAcquisition = context.HttpContext.RequestServices.GetRequiredService<ITokenAcquisition>();
                        var cacheProvider = context.HttpContext.RequestServices.GetRequiredService<IMsalTokenCacheProvider>();
                        var appOptions = new ConfidentialClientApplicationOptions();

                        Configuration.Bind("AzureAd", appOptions);

                        var app = ConfidentialClientApplicationBuilder.Create(appOptions.ClientId)
                                    .WithAuthority(options.Authority)
                                    .WithClientSecret(appOptions.ClientSecret)
                                    .Build();

                        await cacheProvider.InitializeAsync(app.UserTokenCache);

                        var bearerToken = context.HttpContext.Items["JwtSecurityTokenUsedToCallWebAPI"] as JwtSecurityToken;

                        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);

                        if (bearerToken != null && accounts.Count() > 0)
                        {
                            var accountActivity = new MsalAccountActivity(accounts.First(), bearerToken.RawSignature);
                            var repo = context.HttpContext.RequestServices.GetRequiredService<IMsalAccountActivityRepository>();
                            await repo.UpsertActivity(accountActivity);
                        }
                    }
                };
            });

            services.AddDistributedSqlServerCache(options =>
            {
                /*
                    dotnet tool install --global dotnet-sql-cache
                    dotnet sql-cache create "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=MY_TOKEN_CACHE_DATABASE;Integrated Security=True;" dbo TokenCache    
                */
                options.ConnectionString = Configuration.GetConnectionString("TokenCacheDbConnStr");
                options.SchemaName = "dbo";
                options.TableName = "TokenCache";
            });

            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                // Since IdentityModel version 5.2.1 (or since Microsoft.AspNetCore.Authentication.JwtBearer version 2.2.0),
                // PII hiding in log files is enabled by default for GDPR concerns.
                // For debugging/development purposes, one can enable additional detail in exceptions by setting IdentityModelEventSource.ShowPII to true.
                // Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            app.UseDefaultFiles();
            app.UseStaticFiles(); // For the wwwroot folder

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
