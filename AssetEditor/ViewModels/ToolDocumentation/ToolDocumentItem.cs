using System;
using System.IO;

namespace AssetEditor.ViewModels.ToolDocumentation
{
    public sealed class ToolDocumentItem
    {
        public ToolDocumentItem(string fullPath, string rootFolder)
        {
            FullPath = fullPath;
            var relativePath = Path.GetRelativePath(rootFolder, fullPath);
            DisplayName = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            Extension = Path.GetExtension(fullPath).ToLowerInvariant();
        }

        public string FullPath { get; }
        public string DisplayName { get; }
        public string Extension { get; }
        public bool IsMarkdown => Extension is ".md" or ".markdown";
        public bool IsHtml => Extension is ".html" or ".htm";
    }
}
