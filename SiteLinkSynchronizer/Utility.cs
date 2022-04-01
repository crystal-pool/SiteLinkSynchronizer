using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using WikiClientLibrary;
using WikiClientLibrary.Generators;
using WikiClientLibrary.Sites;

namespace SiteLinkSynchronizer
{
    internal static class Utility
    {

        /// <summary>
        /// Merge two ordered sequence.
        /// </summary>
        public static async IAsyncEnumerable<T> OrderedMerge<T>(this IAsyncEnumerable<T> elements1, IAsyncEnumerable<T> elements2, IComparer<T> comparer,
            [EnumeratorCancellation]CancellationToken ct = default)
        {
            if (elements1 == null) throw new ArgumentNullException(nameof(elements1));
            if (elements2 == null) throw new ArgumentNullException(nameof(elements2));
            await using var e1 = elements1.GetAsyncEnumerator(ct);
            await using var e2 = elements2.GetAsyncEnumerator(ct);
            var next1 = await e1.MoveNextAsync(ct);
            var next2 = await e2.MoveNextAsync(ct);
            while (next1 || next2)
            {
                if (!next2 || next1 && comparer.Compare(e1.Current, e2.Current) <= 0)
                {
                    yield return e1.Current;
                    next1 = await e1.MoveNextAsync(ct);
                }
                else
                {
                    yield return e2.Current;
                    next2 = await e2.MoveNextAsync(ct);
                }
            }
        }

        public static string MdMakeUserLink(WikiSite site, string userName)
        {
            var link = WikiLink.Parse(site, userName, BuiltInNamespaces.User);
            var talkLink = WikiLink.Parse(site, userName, BuiltInNamespaces.UserTalk);
            var contribLink = site.SiteInfo.MakeArticleUrl("Special:Contribs/" + userName);
            return string.Format("{0} ({1}|{2})",
                MarkdownUtility.MakeLink(link.Title, link.TargetUrl),
                MarkdownUtility.MakeLink("T", talkLink.TargetUrl),
                MarkdownUtility.MakeLink("C", contribLink));
        }

        public static string MdMakeArticleLink(WikiSite site, string title)
        {
            return MarkdownUtility.MakeLink(title, site.SiteInfo.MakeArticleUrl(title));
        }

    }

    public class LogEventItemTimeStampComparer : Comparer<LogEventItem>
    {

        public new static LogEventItemTimeStampComparer Default { get; } = new LogEventItemTimeStampComparer();

        /// <inheritdoc />
        public override int Compare(LogEventItem x, LogEventItem y)
        {
            Debug.Assert(x != null && y != null);
            if (x.TimeStamp > y.TimeStamp) return 1;
            if (x.TimeStamp < y.TimeStamp) return -1;
            return x.LogId.CompareTo(y.LogId);
        }
    }

}
