using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;
using ILogger = Serilog.ILogger;

namespace SiteLinkSynchronizer;

internal class MyWikiFamily : WikiFamily
{

    private readonly Dictionary<string, CredentialEntry> credentialDict = new Dictionary<string, CredentialEntry>();
    private readonly ILoggerFactory loggerFactory;

    /// <inheritdoc />
    public MyWikiFamily(IWikiClient wikiClient, ILoggerFactory loggerFactory) : base(wikiClient)
    {
        this.loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public MyWikiFamily(IWikiClient wikiClient, ILogger rootLogger) : base(wikiClient)
    {
        this.loggerFactory = new LoggerFactory(new[] { new SerilogLoggerProvider(rootLogger) });
    }


    public void SetCredential(string prefix, string userName, string password)
    {
        credentialDict[prefix] = new CredentialEntry(userName, password);
    }

    /// <inheritdoc />
    protected override async Task<WikiSite> CreateSiteAsync(string prefix, string apiEndpoint)
    {
        var site = await base.CreateSiteAsync(prefix, apiEndpoint);
        site.Logger = loggerFactory.CreateLogger(prefix);

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
