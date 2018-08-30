using System;
using System.Collections.Generic;
using System.Text;

namespace SiteLinkSynchronizer.Configuration
{
    public class WikiSitesConfig
    {

        public IDictionary<string, WikiSiteConfig> WikiSites { get; set; }

    }

    public class WikiSiteConfig
    {
        
        public string ApiEndpoint { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

    }

    public class SynchronizerConfig
    {
        public string RepositorySite { get; set; }

        public IList<string> ClientSites { get; set; }

        public ICollection<int> Namespaces { get; set; }

        public bool WhatIf { get; set; }
        
    }

    public class StateStoreConfig
    {

        public string FileName { get; set; }

    }

    public class DiscordWebhookConfig
    {

        public string Token { get; set; }

        public ulong Id { get; set; }

    }

}
