﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using AsyncEnumerableExtensions;
using WikiClientLibrary.Generators;

namespace SiteLinkSynchronizer
{
    internal static class Utility
    {

        /// <summary>
        /// Merge two ordered sequence.
        /// </summary>
        public static IAsyncEnumerable<T> OrderedMerge<T>(this IAsyncEnumerable<T> elements1, IAsyncEnumerable<T> elements2, IComparer<T> comparer)
        {
            if (elements1 == null) throw new ArgumentNullException(nameof(elements1));
            if (elements2 == null) throw new ArgumentNullException(nameof(elements2));
            return AsyncEnumerableFactory.FromAsyncGenerator<T>(async (sink, ct) =>
            {
                using (var e1 = elements1.GetEnumerator())
                using (var e2 = elements2.GetEnumerator())
                {
                    var next1 = await e1.MoveNext(ct);
                    var next2 = await e2.MoveNext(ct);
                    while (next1 || next2)
                    {
                        if (!next2 || next1 && comparer.Compare(e1.Current, e2.Current) <= 0)
                        {
                            await sink.YieldAndWait(e1.Current);
                            next1 = await e1.MoveNext(ct);
                        }
                        else
                        {
                            await sink.YieldAndWait(e2.Current);
                            next2 = await e2.MoveNext(ct);
                        }
                    }
                }
            });
        }

    }

    public class LogEventItemTimeStampComparer : Comparer<LogEventItem>
    {

        public static LogEventItemTimeStampComparer Default { get; } = new LogEventItemTimeStampComparer();

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
