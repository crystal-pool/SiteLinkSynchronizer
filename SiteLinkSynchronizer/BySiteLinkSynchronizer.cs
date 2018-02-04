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

        public async Task CheckRecentLogs(string clientSiteName, ICollection<int> namespaces)
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
            logger.LogTrace("Checking on site: {Site}, Timestamp: {Timestamp1} ~ {Timestamp2} ({Duration:G}), LastLogId: {StartLogId}.",
                clientSiteName, startTime, endTime, endTime - startTime, lastLogId);
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

            var titleIdCache = new Dictionary<string, string>();

            async Task FetchTitleIds(IEnumerable<string> titles)
            {
                var workTitles = titles.Where(t => !titleIdCache.ContainsKey(t)).ToList();
                var ids = await Entity.IdsFromSiteLinksAsync(repos, clientSiteName, workTitles).ToList();
                for (int i = 0; i < workTitles.Count; i++)
                    titleIdCache[workTitles[i]] = ids[i];
            }

            async Task ProcessBatch(IList<LogEventItem> batch)
            {
                await FetchTitleIds(batch.Select(rc => rc.Title).Distinct());
                foreach (var logEvent in batch)
                {
                    // Article does not have associated item in Wikibase repository
                    var id = titleIdCache[logEvent.Title];
                    if (id == null) continue;
                    var entity = new Entity(repos, id);
                    if (logEvent.Type == LogActions.Move && (logEvent.Action == LogActions.Move || logEvent.Action == LogActions.MoveOverRedirect))
                    {
                        var newTitle = logEvent.Params.TargetTitle;
                        logger.LogInformation("{ItemId} on {Site}: Moved [[{OldTitle}]] -> [[{NewTitle}]]", id, clientSiteName, logEvent.Title, newTitle);
                        if (!WhatIf)
                        {
                            await entity.EditAsync(new[] {new EntityEditEntry(nameof(entity.SiteLinks), new EntitySiteLink(clientSiteName, newTitle))},
                                string.Format("/* clientsitelink-update:0|{0}|{0}:{1}|{0}:{2} */ UserName={3}, LogId={4}",
                                    clientSiteName, logEvent.Title, newTitle, logEvent.UserName, logEvent.LogId),
                                EntityEditOptions.Bot | EntityEditOptions.Bulk);
                        }

                        titleIdCache[newTitle] = id;
                        titleIdCache[logEvent.Title] = null;
                    }
                    else if (logEvent.Type == LogTypes.Delete && logEvent.Action == LogActions.Delete)
                    {
                        logger.LogInformation("{ItemId} on {Site}: Deleted [[{OldTitle}]]", id, clientSiteName, logEvent.Title);
                        if (!WhatIf)
                        {
                            await entity.EditAsync(new[]
                                {
                                    new EntityEditEntry(nameof(entity.SiteLinks), new EntitySiteLink(clientSiteName, "dummy"), EntityEditEntryState.Removed)
                                },
                                string.Format("/* clientsitelink-remove:1||{0} */ UserName={1}, LogId={2}",
                                    clientSiteName, logEvent.UserName, logEvent.LogId),
                                EntityEditOptions.Bot | EntityEditOptions.Bulk);
                        }

                        // Removed
                        titleIdCache[logEvent.Title] = null;
                    }
                }

                stateStore.LeaveTrace(clientSiteName, batch.Last().TimeStamp, batch.Last().LogId);
            }

            using (var ie = logEvents.Buffer(100).GetEnumerator())
            {
                while (await ie.MoveNext())
                {
                    await ProcessBatch(ie.Current);
                }
            }
            stateStore.LeaveTrace(clientSiteName, endTime);
        }

    }
}
