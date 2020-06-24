using DataAccessLayer;
using DataAccessLayer.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DaemonApp
{
    class Program
    {
        private static ServiceProvider _serviceProvider = null;
        private static AuthenticationConfig config = AuthenticationConfig.ReadFromJsonFile("appsettings.json");

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Application started! \n");

                _serviceProvider = new ServiceCollection()
                    .AddLogging()
                    .AddDistributedMemoryCache()
                    .AddDistributedSqlServerCache(options =>
                    {
                        options.ConnectionString = config.TokenCacheDbConnStr;
                        options.SchemaName = "dbo";
                        options.TableName = "TokenCache";
                    })
                    .AddDbContext<CacheDbContext>(options => options.UseSqlServer(config.TokenCacheDbConnStr))
                    .AddScoped<IMsalAccountActivityRepository, MsalAccountActivityRepository>()
                    .BuildServiceProvider();

                while (true)
                {
                    RunAsync().GetAwaiter().GetResult();
                    Thread.Sleep(TimeSpan.FromMinutes(10));
                }
                
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        private static async Task RunAsync()
        {
            var scopes = new string[] { "User.Read" };
            var repository = _serviceProvider.GetRequiredService<IMsalAccountActivityRepository>();
            var accountsToRefresh = await repository.GetAccountsToRefresh();

            Console.WriteLine($"Refreshing activities...");

            IConfidentialClientApplication app;

            // For each IAccount, we need to build a new ConfidentialClientApplication, giving the cache key to the token cache provider,
            // otherwise it wouldn't be able to find which cache key to use since each interation is for a different user.
            foreach (var activity in accountsToRefresh)
            {
                app = await GetConfidentialClientApplication(config, activity.CacheKey);
                var account = new MsalAccount
                {
                    Environment = activity.Environment,
                    Username = activity.Username,
                    HomeAccountId = new AccountId(
                        activity.AccountIdentifier,
                        activity.AccountObjectId,
                        activity.AccountTenantId)
                };

                try
                {
                    var result = await app.AcquireTokenSilent(scopes, account)
                        .ExecuteAsync()
                        .ConfigureAwait(false);
                }
                catch (MsalUiRequiredException ex)
                {
                    // Should we delete this UserTokenActivity in this case, since it needs interaction and the daemon app will not be able to
                    // acquire the token silently?

                    activity.FailedToRefresh = true;
                    await repository.UpsertActivity(activity);

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Account {activity.Username} failed to refresh.");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Task complete.");
            Console.ResetColor();
        }

        private static async Task<IConfidentialClientApplication> GetConfidentialClientApplication(AuthenticationConfig config, string cacheKey)
        {
            var app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                .WithClientSecret(config.ClientSecret)
                .WithAuthority(new Uri(config.Authority))
                .Build();

            var distributedCache = _serviceProvider.GetService<IDistributedCache>();

            MsalSqlTokenCacheProvider cacheProvider = new MsalSqlTokenCacheProvider(distributedCache, cacheKey);

            await cacheProvider.InitializeAsync(app.UserTokenCache);

            return app;

        }
    }
}
