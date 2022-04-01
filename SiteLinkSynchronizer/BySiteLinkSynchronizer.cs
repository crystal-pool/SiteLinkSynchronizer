using System.Diagnostics;
using Serilog;
using Serilog.Core;
using SiteLinkSynchronizer.States;
using WikiClientLibrary;
using WikiClientLibrary.Generators;
using WikiClientLibrary.Generators.Primitive;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;

namespace SiteLinkSynchronizer;

public class BySiteLinkSynchronizer
{

    private readonly IWikiFamily family;
    private readonly StateStore stateStore;
    private readonly DiscordWebhookMessenger messenger;
    private readonly ILogger logger;

    public BySiteLinkSynchronizer(ILogger rootLogger, IWikiFamily family, StateStore stateStore, DiscordWebhookMessenger messenger)
    {
        if (family == null) throw new ArgumentNullException(nameof(family));
        logger = rootLogger.ForContext(Constants.SourceContextPropertyName, "LinkSynchronizer");
        this.family = family;
        this.stateStore = stateStore;
        this.messenger = messenger;
    }

    public string RepositorySiteName { get; set; }

    /// <summary>
    /// For how far ago can we set our earliest StartTime.
    /// </summary>
    public TimeSpan MaxTracebackDuration { get; set; } = TimeSpan.FromDays(120);

    /// <summary>
    /// For how long in time can we check for logs during each session.
    /// </summary>
    public TimeSpan MaxCheckDuration { get; set; } = TimeSpan.FromDays(60);

    public TimeSpan StatusReportInterval { get; set; } = TimeSpan.FromMinutes(5);

    public bool WhatIf { get; set; }

    public async Task CheckRecentLogsSafeAsync(ICollection<string> clientSiteNames, ICollection<int> namespaces)
    {
        logger.Information("Checking on {SiteCount} site(s), {Flags}", clientSiteNames.Count, WhatIf ? "[WhatIf]" : null);
        messenger.PushMessage("Checking on {0} site(s). {1}", clientSiteNames.Count, WhatIf ? "[WhatIf]" : null);
        foreach (var clientSiteName in clientSiteNames)
        {
            await CheckRecentLogsSafeAsync(clientSiteName, namespaces);
        }
        logger.Information("Checking finished.");
        messenger.PushMessage("Checking finished.");
    }

    public async Task CheckRecentLogsSafeAsync(string clientSiteName, ICollection<int> namespaces)
    {
        try
        {
            await CheckRecentLogsAsync(clientSiteName, namespaces);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Exception while checking on {SiteName}.", clientSiteName);
            messenger.PushMessage("Exception while checking on {0}. {1}: {2}", clientSiteName, ex.GetType(), ex.Message);
            if (ex is MediaWikiRemoteException remoteEx && !string.IsNullOrEmpty(remoteEx.RemoteStackTrace))
            {
                messenger.PushMessage("Remote stack trace: " + remoteEx.RemoteStackTrace);
            }
        }
    }

    public async Task CheckRecentLogsAsync(string clientSiteName, ICollection<int> namespaces)
    {
        var repos = await family.GetSiteAsync(RepositorySiteName);
        var reposSiteInfo = WikibaseSiteInfo.FromSiteInfo(repos.SiteInfo);
        var client = await family.GetSiteAsync(clientSiteName);
        var startTime = DateTime.UtcNow - MaxTracebackDuration;
        var lastLogId = -1;
        {
            var trace = stateStore.GetTrace(clientSiteName);
            if (trace != null)
            {
                startTime = trace.NextStartTime;
                lastLogId = trace.LastLogId;
            }
        }
        var endTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        if (endTime - startTime > MaxCheckDuration)
        {
            logger.Warning("Max check duration reached on {Site}.", clientSiteName);
            messenger.PushMessage("Max check duration reached on {0}.", clientSiteName);
            endTime = startTime + MaxCheckDuration;
        }
        logger.Information("Checking on {Site}, {Timestamp1} ~ {Timestamp2} ({Duration:G}), LastLogId: {StartLogId}, {Flags}",
            clientSiteName, startTime, endTime, endTime - startTime, lastLogId, WhatIf ? "[W]" : null);
        //messenger.PushMessage("Checking on {0}, {1:u} ~ {2:u} ({3:g}) {4}",
        //    clientSiteName, startTime, endTime, endTime - startTime, WhatIf ? "[W]" : null);
        var elapsedSw = Stopwatch.StartNew();
        var statusReportSw = Stopwatch.StartNew();
        var processedRawLogCount = 0;
        var processedRawLogTimestamp = DateTime.MinValue;
        var processedLogCount = 0;
        var processedLogTimestamp = DateTime.MinValue;

        void CheckReportStatus()
        {
            if (statusReportSw.Elapsed >= StatusReportInterval)
            {
                logger.Information(
                    "Processed {LogCount} ({RawLogCount} raw) logs on {Site} used {TimeSpan}, last log timestamp: {Timestamp} ({RawTimestamp} raw).",
                    processedLogCount, processedRawLogCount, clientSiteName, elapsedSw.Elapsed, processedLogTimestamp, processedRawLogTimestamp);
                messenger.PushMessage("Processed {0} ({1} raw) logs on {2} used {3:g}, last at: {4:u} ({5:u} raw).",
                    processedLogCount, processedRawLogCount, clientSiteName, elapsedSw.Elapsed, processedLogTimestamp, processedRawLogTimestamp);
                statusReportSw.Restart();
            }
        }

        IAsyncEnumerable<LogEventItem> logEvents = null;
        var wikiListCompatOptions = new WikiListCompatibilityOptions { ContinuationLoopBehaviors = WikiListContinuationLoopBehaviors.FetchMore };
        if (client.SiteInfo.Version >= new MediaWikiVersion(1, 24))
        {
            foreach (var ns in namespaces)
            {
                var moveList = new LogEventsList(client)
                {
                    CompatibilityOptions = wikiListCompatOptions,
                    StartTime = startTime,
                    EndTime = endTime,
                    TimeAscending = true,
                    NamespaceId = ns,
                    LogType = LogTypes.Move,
                    PaginationSize = 200,
                };
                var deleteList = new LogEventsList(client)
                {
                    CompatibilityOptions = wikiListCompatOptions,
                    StartTime = startTime,
                    EndTime = endTime,
                    TimeAscending = true,
                    NamespaceId = ns,
                    LogType = LogTypes.Delete,
                    PaginationSize = 200,
                };
                if (logEvents == null)
                    logEvents = moveList.EnumItemsAsync();
                else
                    logEvents = logEvents.OrderedMerge(moveList.EnumItemsAsync(), LogEventItemTimeStampComparer.Default);
                logEvents = logEvents.OrderedMerge(deleteList.EnumItemsAsync(), LogEventItemTimeStampComparer.Default);
            }
        }
        else
        {
            // MW 1.19 does not support LogEventsList.NamespaceId. Pity.
            var moveList = new LogEventsList(client)
            {
                CompatibilityOptions = wikiListCompatOptions,
                StartTime = startTime,
                EndTime = endTime,
                TimeAscending = true,
                LogType = LogTypes.Move,
                PaginationSize = 200,
            };
            var deleteList = new LogEventsList(client)
            {
                CompatibilityOptions = wikiListCompatOptions,
                StartTime = startTime,
                EndTime = endTime,
                TimeAscending = true,
                LogType = LogTypes.Delete,
                PaginationSize = 200,
            };
            logEvents = moveList.EnumItemsAsync()
                .Select((e, i) =>
                {
                    processedRawLogCount = i;
                    processedRawLogTimestamp = e.TimeStamp;
                    CheckReportStatus();
                    return e;
                })
                .Where(e => namespaces.Contains(e.NamespaceId))
                .OrderedMerge(deleteList.EnumItemsAsync().Where(e => namespaces.Contains(e.NamespaceId)), LogEventItemTimeStampComparer.Default);
        }

        if (logEvents == null) return;
        logEvents = logEvents.Where(e => e.LogId > lastLogId);

        var articleState = new SiteArticleStateContainer(clientSiteName, logger);

        async Task FetchTitleIds(IEnumerable<string> titles)
        {
            var workTitles = titles.Where(t => !articleState.ContainsArticleTitle(t)).ToList();
            var ids = await Entity.IdsFromSiteLinksAsync(repos, clientSiteName, workTitles).ToListAsync();
            for (int i = 0; i < workTitles.Count; i++)
            {
                if (ids[i] != null)
                {
                    // In case we already know this ID, and have moved it around in our state container.
                    if (!articleState.ContainsEntityId(ids[i]))
                        articleState.AddEntityArticle(ids[i], workTitles[i]);
                }
                else
                {
                    articleState.AddTrivialArticle(workTitles[i]);
                }
            }
        }

        async Task ProcessBatch(IList<LogEventItem> batch)
        {
            await FetchTitleIds(batch.Select(rc => rc.Title).Distinct());
            foreach (var logEvent in batch)
            {
                // Article does not have associated item in Wikibase repository
                var id = articleState.EntityIdFromArticleTitle(logEvent.Title);
                if (logEvent.Type == LogActions.Move && (logEvent.Action == LogActions.Move || logEvent.Action == LogActions.MoveOverRedirect))
                {
                    var newTitle = logEvent.Params.TargetTitle;
                    if (id != null)
                    {
                        logger.Information("{ItemId} on {Site}: {UserName} moved [[{OldTitle}]] -> [[{NewTitle}]]. {Flags}",
                            id, clientSiteName, logEvent.UserName,
                            logEvent.Title, newTitle,
                            logEvent.Params.SuppressRedirect ? "[SuppressRedirect]" : "");
                        messenger.PushMessage("{0} on {1}: {2:u} {3} moved ~~{4}~~ to {5}. {6}",
                            MarkdownUtility.MakeLink(id, reposSiteInfo.MakeEntityUri(id)),
                            clientSiteName,
                            logEvent.TimeStamp,
                            Utility.MdMakeUserLink(client, logEvent.UserName),
                            Utility.MdMakeArticleLink(client, logEvent.Title),
                            Utility.MdMakeArticleLink(client, newTitle),
                            logEvent.Params.SuppressRedirect ? "Redirect is suppressed." : "");
                    }
                    articleState.Move(logEvent.Title, newTitle, logEvent.Params.SuppressRedirect,
                        string.Format("/* clientsitelink-update:0|{0}|{0}:{1}|{0}:{2} */ UserName={3}, LogId={4}",
                            clientSiteName, logEvent.Title, newTitle, logEvent.UserName, logEvent.LogId));
                }
                else if (logEvent.Type == LogTypes.Delete && logEvent.Action == LogActions.Delete)
                {
                    if (id != null)
                    {
                        logger.Information("{ItemId} on {Site}: {UserName} deleted [[{OldTitle}]]",
                            id, clientSiteName, logEvent.UserName, logEvent.Title);
                        messenger.PushMessage("{0} on {1}: {2:u} {3} deleted ~~{4}~~.",
                            MarkdownUtility.MakeLink(id, reposSiteInfo.MakeEntityUri(id)),
                            clientSiteName,
                            logEvent.TimeStamp,
                            Utility.MdMakeUserLink(client, logEvent.UserName),
                            Utility.MdMakeArticleLink(client, logEvent.Title));
                    }
                    articleState.Delete(logEvent.Title,
                        string.Format("/* clientsitelink-remove:1||{0} */ UserName={1}, LogId={2}",
                            clientSiteName, logEvent.UserName, logEvent.LogId));
                }
            }
        }

        var processedLogId = lastLogId;

        await foreach (var batch in logEvents.Buffer(100))
        {
            await ProcessBatch(batch);
            processedLogId = batch.Last().LogId;
            processedLogTimestamp = batch.Last().TimeStamp;
            processedLogCount += batch.Count;
            CheckReportStatus();
        }

        int updateCounter = 0;
        foreach (var op in articleState.EnumEntityOperations())
        {
            logger.Debug("Change site link of {EntityId} on {SiteName}: [[{OldTitle}]] -> [[{NewTitle}]]",
                op.EntityId, clientSiteName, op.OldArticleTitle, op.ArticleTitle);
            var entity = new Entity(repos, op.EntityId);
            if (!WhatIf)
            {
                await entity.EditAsync(new[]
                {
                    new EntityEditEntry(nameof(entity.SiteLinks),
                        new EntitySiteLink(clientSiteName, op.ArticleTitle),
                        op.ArticleTitle == null ? EntityEditEntryState.Removed : EntityEditEntryState.Updated)
                }, op.Comment, EntityEditOptions.Bot | EntityEditOptions.Bulk);
            }

            updateCounter++;
        }

        if (!WhatIf)
            stateStore.LeaveTrace(clientSiteName, endTime, processedLogId);

        if (updateCounter == 0)
        {
            logger.Debug("No updates for {SiteName}.", clientSiteName);
        }
        else
        {
            if (WhatIf)
            {
                logger.Information("Should update {Count} site link(s) for {SiteName}.", updateCounter, clientSiteName);
                messenger.PushMessage("Should update {0} site link(s) for {1}.", updateCounter, clientSiteName);
            }
            else
            {
                logger.Information("Updated {Count} site link(s) for {SiteName}.", updateCounter, clientSiteName);
                messenger.PushMessage("Updated {0} site link(s) for {1}.", updateCounter, clientSiteName);
            }
        }
    }

}
