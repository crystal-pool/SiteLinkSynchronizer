using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SiteLinkSynchronizer
{
    internal static class MarkdownUtility
    {

        private static readonly char[] markdownEscapedCharacters = @"|*#{}[]()\".ToCharArray();
        private static readonly Regex markdownEscapedCharactersMatcher = new Regex("[" + Regex.Escape(new string(markdownEscapedCharacters)) + "]");

        public static string Escape(string text)
        {
            if (text.IndexOfAny(markdownEscapedCharacters) < 0) return text;
            return markdownEscapedCharactersMatcher.Replace(text, @"\$0");
        }

        public static string MakeLink(string text, string url)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            sb.Append(Escape(text));
            sb.Append("](<");
            sb.Append(Escape(url));
            sb.Append(">)");
            return sb.ToString();
        }

    }
}
