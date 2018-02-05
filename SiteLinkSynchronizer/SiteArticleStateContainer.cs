using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SiteLinkSynchronizer
{
    /// <summary>
    /// Used to reduce/simplify a batch of article move/delete operations.
    /// </summary>
    public class SiteArticleStateContainer
    {

        private readonly ILogger logger;

        // entity ID --> article
        private readonly Dictionary<string, KnownArticle> entityArticleDict = new Dictionary<string, KnownArticle>();
        // currentArticleTitle --> article
        private readonly Dictionary<string, KnownArticle> articleStateDict = new Dictionary<string, KnownArticle>();
        
        public SiteArticleStateContainer(ILogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Let the container know this title exists, but it does not have any associated entity ID information.
        /// </summary>
        public void AddTrivialArticle(string title)
        {
            if (title == null) throw new ArgumentNullException(nameof(title));
            // Use null as placeholder for trivial articles that does not have associated entity ID information.
            articleStateDict.Add(title, null);
        }

        /// <summary>
        /// Registers a new entity ID -- article title pair.
        /// </summary>
        public void AddEntityArticle(string entityId, string title)
        {
            if (entityId == null) throw new ArgumentNullException(nameof(entityId));
            if (title == null) throw new ArgumentNullException(nameof(title));
            var article = new KnownArticle(entityId, title);
            entityArticleDict.Add(entityId, article);
            articleStateDict.Add(title, article);
        }

        public bool ContainsArticleTitle(string title)
        {
            return articleStateDict.ContainsKey(title);
        }

        public string EntityIdFromArticleTitle(string title)
        {
            // This case may happen when an article has been moved, with redirect left,
            // Then the old redirect got moved or deleted.
            if (!articleStateDict.TryGetValue(title, out var article)) return null;
            return article?.EntityId;
        }

        public bool Move(string oldTitle, string newTitle, string comment)
        {
            if (!articleStateDict.Remove(oldTitle, out var article)) return false;
            if (article != null)
            {
                article.NewTitle = newTitle;
                article.Comments.Add(comment);
                if (articleStateDict.ContainsKey(newTitle))
                {
                    // This shouldn't happen, because the page of destination title should be deleted beforehand.
                    logger.LogWarning("An existing page [[{DestTitle}]] is overwritten without deletion from [[{SrcTitle}]].", newTitle, oldTitle);
                }
            }

            articleStateDict[newTitle] = article;
            return true;
        }

        public bool Delete(string title, string comment)
        {
            if (!articleStateDict.Remove(title, out var article)) return false;
            if (article != null)
            {
                // Make it disappear
                article.NewTitle = null;
            }

            return true;
        }

        public IEnumerable<EntityOperation> EnumEntityOperations()
        {
            foreach (var p in entityArticleDict)
            {
                // In case a page is moved elsewhere before it is finally moved back to the original title.
                if (p.Value.OldTitle == p.Value.NewTitle) continue;
                yield return new EntityOperation(p.Key, p.Value.OldTitle, p.Value.NewTitle, string.Join("; ", p.Value.Comments));
            }
        }

        private class KnownArticle
        {

            public readonly string EntityId;

            public readonly string OldTitle;

            public string NewTitle;

            public readonly List<string> Comments = new List<string>();

            public KnownArticle(string entityId, string oldTitle)
            {
                EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
                OldTitle = oldTitle ?? throw new ArgumentNullException(nameof(oldTitle));
            }
        }

        public class EntityOperation
        {
            public EntityOperation(string entityId, string oldArticleTitle, string articleTitle, string comment)
            {
                EntityId = entityId;
                OldArticleTitle = oldArticleTitle;
                ArticleTitle = articleTitle;
                Comment = comment;
            }

            public string EntityId { get; }

            public string OldArticleTitle { get; }

            public string ArticleTitle { get; }

            public string Comment { get; }

        }

    }

}
