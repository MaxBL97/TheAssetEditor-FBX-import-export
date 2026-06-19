using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetEditor.ViewModels.ToolDocumentation
{
    public static class ToolDocumentationRenderer
    {
        public static string Render(ToolDocumentItem? document)
        {
            if (document == null)
                return CreateShell("Tool documentation", "<p>Select a document from the left list.</p>", null);

            if (!File.Exists(document.FullPath))
                return CreateShell("Tool documentation", "<p>The selected document no longer exists.</p>", null);

            var source = File.ReadAllText(document.FullPath, Encoding.UTF8);
            var baseUri = new Uri(Path.GetDirectoryName(document.FullPath)! + Path.DirectorySeparatorChar).AbsoluteUri;

            if (document.IsHtml)
                return InjectHtmlShell(source, baseUri);

            if (document.IsMarkdown)
                return CreateShell(document.DisplayName, ConvertMarkdownToHtml(source), baseUri);

            return CreateShell(document.DisplayName, $"<pre>{Html(source)}</pre>", baseUri);
        }

        private static string InjectHtmlShell(string html, string baseUri)
        {
            var baseTag = $"<base href=\"{HtmlAttr(baseUri)}\" />";
            var mermaid = MermaidScript();

            if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
                html = Regex.Replace(html, "</head>", baseTag + DefaultCss() + mermaid + "</head>", RegexOptions.IgnoreCase);
            else
                html = baseTag + DefaultCss() + mermaid + html;

            return html;
        }

        private static string ConvertMarkdownToHtml(string markdown)
        {
            var sb = new StringBuilder();
            var inFence = false;
            var fenceLanguage = string.Empty;
            var fenceBuilder = new StringBuilder();
            var inUl = false;
            var inOl = false;

            void CloseLists()
            {
                if (inUl)
                {
                    sb.AppendLine("</ul>");
                    inUl = false;
                }
                if (inOl)
                {
                    sb.AppendLine("</ol>");
                    inOl = false;
                }
            }

            foreach (var rawLine in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    if (!inFence)
                    {
                        CloseLists();
                        inFence = true;
                        fenceLanguage = trimmed.Length > 3 ? trimmed[3..].Trim().ToLowerInvariant() : string.Empty;
                        fenceBuilder.Clear();
                    }
                    else
                    {
                        var content = Html(fenceBuilder.ToString().TrimEnd('\n'));
                        if (fenceLanguage == "mermaid")
                            sb.AppendLine($"<pre class=\"mermaid\">{content}</pre>");
                        else
                            sb.AppendLine($"<pre><code>{content}</code></pre>");

                        inFence = false;
                        fenceLanguage = string.Empty;
                    }
                    continue;
                }

                if (inFence)
                {
                    fenceBuilder.AppendLine(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    CloseLists();
                    continue;
                }

                var headingLevel = CountHeadingLevel(trimmed);
                if (headingLevel > 0)
                {
                    CloseLists();
                    var text = trimmed[headingLevel..].Trim();
                    sb.AppendLine($"<h{headingLevel}>{Inline(text)}</h{headingLevel}>");
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    if (inOl)
                    {
                        sb.AppendLine("</ol>");
                        inOl = false;
                    }
                    if (!inUl)
                    {
                        sb.AppendLine("<ul>");
                        inUl = true;
                    }
                    sb.AppendLine($"<li>{Inline(trimmed[2..].Trim())}</li>");
                    continue;
                }

                var orderedMatch = Regex.Match(trimmed, "^\\d+\\.\\s+(.+)$");
                if (orderedMatch.Success)
                {
                    if (inUl)
                    {
                        sb.AppendLine("</ul>");
                        inUl = false;
                    }
                    if (!inOl)
                    {
                        sb.AppendLine("<ol>");
                        inOl = true;
                    }
                    sb.AppendLine($"<li>{Inline(orderedMatch.Groups[1].Value.Trim())}</li>");
                    continue;
                }

                CloseLists();
                sb.AppendLine($"<p>{Inline(trimmed)}</p>");
            }

            CloseLists();
            return sb.ToString();
        }

        private static int CountHeadingLevel(string line)
        {
            var count = 0;
            while (count < line.Length && count < 6 && line[count] == '#')
                count++;

            return count > 0 && count < line.Length && line[count] == ' ' ? count : 0;
        }

        private static string Inline(string text)
        {
            var value = Html(text);
            value = Regex.Replace(value, "`([^`]+)`", "<code>$1</code>");
            value = Regex.Replace(value, "\\*\\*([^*]+)\\*\\*", "<strong>$1</strong>");
            value = Regex.Replace(value, "\\[([^\\]]+)\\]\\(([^)]+)\\)", match =>
            {
                var label = match.Groups[1].Value;
                var url = HtmlAttr(match.Groups[2].Value);
                return $"<a href=\"{url}\">{label}</a>";
            });
            return value;
        }

        private static string CreateShell(string title, string body, string? baseUri)
        {
            var baseTag = string.IsNullOrWhiteSpace(baseUri) ? string.Empty : $"<base href=\"{HtmlAttr(baseUri)}\" />";
            return """
<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<meta http-equiv="X-UA-Compatible" content="IE=edge" />
""" + baseTag + DefaultCss() + MermaidScript() + """
<title>Tool documentation</title>
</head>
<body>
""" + body + """
</body>
</html>
""";
        }

        private static string DefaultCss() => """
<style>
:root { color-scheme: dark light; }
body { margin: 0; padding: 24px; font-family: Segoe UI, Arial, sans-serif; font-size: 14px; line-height: 1.5; background: #1e1e1e; color: #e6e6e6; }
h1, h2, h3, h4 { color: #ffffff; margin-top: 1.3em; margin-bottom: .5em; }
h1 { font-size: 30px; border-bottom: 1px solid #555; padding-bottom: 8px; }
h2 { font-size: 22px; border-bottom: 1px solid #444; padding-bottom: 5px; }
h3 { font-size: 17px; }
a { color: #78b7ff; }
pre { background: #111; border: 1px solid #444; border-radius: 4px; padding: 12px; overflow: auto; }
code { font-family: Consolas, Cascadia Mono, monospace; }
:not(pre) > code { background: #111; border: 1px solid #444; border-radius: 3px; padding: 1px 4px; }
table { border-collapse: collapse; width: 100%; margin: 12px 0; }
th, td { border: 1px solid #555; padding: 6px 8px; }
th { background: #2a2a2a; }
.mermaid { background: #f6f6f6; color: #111; }
</style>
""";

        private static string MermaidScript() => """
<script type="module">
import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.esm.min.mjs';
mermaid.initialize({ startOnLoad: true, theme: 'default', securityLevel: 'loose' });
</script>
""";

        private static string Html(string value) => WebUtility.HtmlEncode(value);
        private static string HtmlAttr(string value) => WebUtility.HtmlEncode(value).Replace("\"", "&quot;");
    }
}
