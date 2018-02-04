using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;

namespace SiteLinkSynchronizer
{
    internal class MyWikiFamily : WikiFamily
    {

        private readonly Dictionary<string, CredentialEntry> credentialDict = new Dictionary<string, CredentialEntry>();

        /// <inheritdoc />
        public MyWikiFamily(IWikiClient wikiClient, string name) : base(wikiClient, name)
        {
        }

        public void SetCredential(string prefix, string userName, string password)
        {
            credentialDict[prefix] = new CredentialEntry(userName, password);
        }

        /// <inheritdoc />
        protected override async Task<WikiSite> CreateSiteAsync(string prefix, string apiEndpoint)
        {
            var site = await base.CreateSiteAsync(prefix, apiEndpoint);
            if (credentialDict.TryGetValue(prefix, out var cred))
            {
                site.AccountAssertionFailureHandler = cred;
                await cred.Login(site);
            }

            return site;
        }

        private class CredentialEntry : IAccountAssertionFailureHandler
        {
            public CredentialEntry(string userName, string password)
            {
                UserName = userName;
                Password = password;
            }

            public string UserName { get; }

            public string Password { get; }

            /// <inheritdoc />
            public async Task<bool> Login(WikiSite site)
            {
                await site.LoginAsync(UserName, Password);
                return true;
            }
        }

    }
}
