using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SiteLinkSynchronizer.States;
using WikiClientLibrary.Generators;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;

namespace SiteLinkSynchronizer
{
    public class BySiteLinkSynchronizer
    {

        private readonly IWikiFamily family;
        private readonly StateStore stateStore;
        private readonly ILogger logger;

        public BySiteLinkSynchronizer(ILoggerFactory loggerFactory, IWikiFamily family, StateStore stateStore)
        {
            if (family == null) throw new ArgumentNullException(nameof(family));
            logger = loggerFactory.CreateLogger<BySiteLinkSynchronizer>();
            this.family = family;
            this.stateStore = stateStore;
        }

        public string RepositorySiteName { get; set; }

        public DateTime MinLastCheckedTime { get; set; } = DateTime.UtcNow - TimeSpan.FromDays(30);

        public bool WhatIf { get; set; }

        public async Task CheckRecentLogsSafeAsync(ICollection<string> clientSiteNames, ICollection<int> namespaces)
        {
            logger.LogInformation("Checking on {SiteCount} site(s), {Flags}", clientSiteNames.Count, WhatIf ? "[WhatIf]" : null);
            try
            {
                foreach (var clientSiteName in clientSiteNames)
                {
                    await CheckRecentLogsSafeAsync(clientSiteName, namespaces);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception while checking on {SiteName}.", clientSiteNames);
            }
        }

        public async Task CheckRecentLogsSafeAsync(string clientSiteName, ICollection<int> namespaces)
        {
            try
            {
                await CheckRecentLogsAsync(clientSiteName, namespaces);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception while checking on {SiteName}.", clientSiteName);
            }
        }

        public async Task CheckRecentLogsAsync(string clientSiteName, ICollection<int> namespaces)
        {
            var repos = await family.GetSiteAsync(RepositorySiteName);
            var client = await family.GetSiteAsync(clientSiteName);
            var startTime = MinLastCheckedTime;
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
            logger.LogDebug("Checking on site: {Site}, Timestamp: {Timestamp1} ~ {Timestamp2} ({Duration:G}), LastLogId: {StartLogId}, {Flags}",
                clientSiteName, startTime, endTime, endTime - startTime, lastLogId, WhatIf ? "[W]" : null);
            IAsyncEnumerable<LogEventItem> logEvents = null;
            if (client.SiteInfo.Version >= new Version(1, 24))
            {
                foreach (var ns in namespaces)
                {
                    var moveList = new LogEventsList(client)
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        TimeAscending = true,
                        NamespaceId = ns,
                        LogType = LogTypes.Move,
                        PaginationSize = 100,
                    };
                    var deleteList = new LogEventsList(client)
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        TimeAscending = true,
                        NamespaceId = ns,
                        LogType = LogTypes.Delete,
                        PaginationSize = 100,
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
                // Does not support LogEventsList.NamespaceId
                var moveList = new LogEventsList(client)
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    TimeAscending = true,
                    LogType = LogTypes.Move,
                    PaginationSize = 100,
                };
                var deleteList = new LogEventsList(client)
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    TimeAscending = true,
                    LogType = LogTypes.Delete,
                    PaginationSize = 100,
                };
                logEvents = moveList.EnumItemsAsync().Where(e => namespaces.Contains(e.NamespaceId))
                    .OrderedMerge(deleteList.EnumItemsAsync().Where(e => namespaces.Contains(e.NamespaceId)), LogEventItemTimeStampComparer.Default);
            }

            if (logEvents == null) return;
            logEvents = logEvents.Where(e => e.LogId > lastLogId);

            var articleState = new SiteArticleStateContainer(clientSiteName, logger);

            async Task FetchTitleIds(IEnumerable<string> titles)
            {
                var workTitles = titles.Where(t => !articleState.ContainsArticleTitle(t)).ToList();
                var ids = await Entity.IdsFromSiteLinksAsync(repos, clientSiteName, workTitles).ToList();
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
                            logger.LogInformation("{ItemId} on {Site}: {UserName} moved [[{OldTitle}]] -> [[{NewTitle}]]. {Flags}",
                                id, clientSiteName, logEvent.UserName, logEvent.Title, newTitle,
                                logEvent.Params.SuppressRedirect ? "[SuppressRedirect]" : "");
                        }
                        articleState.Move(logEvent.Title, newTitle, logEvent.Params.SuppressRedirect,
                            string.Format("/* clientsitelink-update:0|{0}|{0}:{1}|{0}:{2} */ UserName={3}, LogId={4}",
                                clientSiteName, logEvent.Title, newTitle, logEvent.UserName, logEvent.LogId));
                    }
                    else if (logEvent.Type == LogTypes.Delete && logEvent.Action == LogActions.Delete)
                    {
                        if (id != null)
                        {
                            logger.LogInformation("{ItemId} on {Site}: {UserName} deleted [[{OldTitle}]]",
                                id, clientSiteName, logEvent.UserName, logEvent.Title);
                        }
                        articleState.Delete(logEvent.Title,
                            string.Format("/* clientsitelink-remove:1||{0} */ UserName={1}, LogId={2}",
                                clientSiteName, logEvent.UserName, logEvent.LogId));
                    }
                }

            }

            var processedLogId = lastLogId;
            using (var ie = logEvents.Buffer(100).GetEnumerator())
            {
                while (await ie.MoveNext())
                {
                    await ProcessBatch(ie.Current);
                    processedLogId = ie.Current.Last().LogId;
                }
            }

            if (!WhatIf)
                stateStore.LeaveTrace(clientSiteName, endTime, processedLogId);
            int updateCounter = 0;
            foreach (var op in articleState.EnumEntityOperations())
            {
                logger.LogDebug("Change site link of {EntityId} on {SiteName}: [[{OldTitle}]] -> [[{NewTitle}]]",
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

            if (updateCounter == 0)
            {
                logger.LogDebug("No updates for {SiteName}.", clientSiteName);
            }
            else
            {
                if (WhatIf)
                    logger.LogInformation("Should update {Count} site links for {SiteName}.", updateCounter, clientSiteName);
                else
                    logger.LogInformation("Updated {Count} site links for {SiteName}.", updateCounter, clientSiteName);
            }
        }
    }
}
