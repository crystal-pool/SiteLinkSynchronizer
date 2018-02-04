using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiteLinkSynchronizer.Configuration;
using SiteLinkSynchronizer.States;
using WikiClientLibrary.Bots;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;

namespace SiteLinkSynchronizer
{
    internal static class Program
    {
        internal static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("config.json")
                .AddJsonFile("config._private.json", optional: true)
                .Build();
            services.Configure<WikiSitesConfig>(config);
            services.Configure<SynchronizerConfig>(config.GetSection("Synchronizer"));
            services.Configure<StateStoreConfig>(config.GetSection("StateStore"));

            services.AddLogging(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddConsole());

            services.AddSingleton<StateStore>();

            services.AddTransient<BySiteLinkSynchronizer>();
            services.AddSingleton<IWikiClient>(sp => new WikiClient
            {
                ClientUserAgent = WikiClientUtility.BuildUserAgent(typeof(Program).Assembly)
            });
            services.AddSingleton<IWikiFamily>(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<WikiSitesConfig>>();
                var inst = new MyWikiFamily(sp.GetRequiredService<IWikiClient>(), null);
                foreach (var site in opt.Value.WikiSites)
                {
                    inst.Register(site.Key, site.Value.ApiEndpoint);
                    if (!string.IsNullOrWhiteSpace(site.Value.UserName))
                        inst.SetCredential(site.Key, site.Value.UserName, site.Value.Password);
                }
                return inst;
            });

            using (var sp = services.BuildServiceProvider())
            {
                var opt = sp.GetRequiredService<IOptions<SynchronizerConfig>>().Value;
                var synchronizer = sp.GetRequiredService<BySiteLinkSynchronizer>();
                synchronizer.RepositorySiteName = opt.RepositorySite;
                synchronizer.WhatIf = opt.WhatIf;
                foreach (var site in opt.ClientSites)
                {
                    await synchronizer.CheckRecentLogs(site, opt.Namespaces);
                }
            }
        }
    }
}
