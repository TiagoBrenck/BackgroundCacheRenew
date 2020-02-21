using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.TokenCacheProviders;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DaemonApp
{
    public class MsalSqlTokenCacheProvider : IMsalTokenCacheProvider
    {
        private readonly IDistributedCache _distributedCache;
        private readonly string _cacheKey;

        public MsalSqlTokenCacheProvider(IDistributedCache distributedCache, string cacheKey)
        {
            _distributedCache = distributedCache;
            _cacheKey = cacheKey;
        }

        public Task ClearAsync()
        {
            throw new NotImplementedException();
        }

        public Task InitializeAsync(ITokenCache tokenCache)
        {
            tokenCache.SetBeforeAccessAsync(OnBeforeAccessAsync);
            tokenCache.SetAfterAccessAsync(OnAfterAccessAsync);
            tokenCache.SetBeforeWriteAsync(OnBeforeWriteAsync);

            return Task.CompletedTask;
        }

        private async Task OnBeforeAccessAsync(TokenCacheNotificationArgs args)
        {
            //string cacheKey = GetCacheKey();

            if (!string.IsNullOrEmpty(_cacheKey))
            {
                byte[] tokenCacheBytes = await ReadCacheBytesAsync(_cacheKey).ConfigureAwait(false);
                args.TokenCache.DeserializeMsalV3(tokenCacheBytes, shouldClearExistingCache: true);
            }
        }

        protected virtual Task OnBeforeWriteAsync(TokenCacheNotificationArgs args)
        {
            return Task.CompletedTask;
        }

        private async Task OnAfterAccessAsync(TokenCacheNotificationArgs args)
        {
            // if the access operation resulted in a cache update
            if (args.HasStateChanged)
            {
                //string cacheKey = GetCacheKey(args.IsApplicationCache);
                if (!string.IsNullOrWhiteSpace(_cacheKey))
                {
                    await WriteCacheBytesAsync(_cacheKey, args.TokenCache.SerializeMsalV3()).ConfigureAwait(false);
                }
            }
        }

        protected async Task<byte[]> ReadCacheBytesAsync(string cacheKey)
        {
            return await _distributedCache.GetAsync(cacheKey).ConfigureAwait(false);
        }

        protected async Task WriteCacheBytesAsync(string cacheKey, byte[] bytes)
        {
            await _distributedCache.SetAsync(cacheKey, bytes).ConfigureAwait(false);
        }
    }
}
