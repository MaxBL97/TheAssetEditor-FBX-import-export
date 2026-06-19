using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;

namespace AssetEditor.ViewModels.ToolDocumentation
{
    public sealed partial class ToolDocumentationViewModel : INotifyPropertyChanged
    {
        private ToolDocumentItem? _selectedDocument;
        private string _renderedHtml = string.Empty;

        public ToolDocumentationViewModel()
        {
            RootFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ToolDoc");
            Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ToolDocumentItem> Documents { get; } = [];
        public string RootFolder { get; }

        public ToolDocumentItem? SelectedDocument
        {
            get => _selectedDocument;
            set
            {
                if (Equals(_selectedDocument, value))
                    return;

                _selectedDocument = value;
                OnPropertyChanged();
                RenderedHtml = ToolDocumentationRenderer.Render(_selectedDocument);
            }
        }

        public string RenderedHtml
        {
            get => _renderedHtml;
            private set
            {
                if (_renderedHtml == value)
                    return;

                _renderedHtml = value;
                OnPropertyChanged();
            }
        }

        [RelayCommand]
        private void Refresh()
        {
            Directory.CreateDirectory(RootFolder);
            Documents.Clear();

            var files = Directory
                .EnumerateFiles(RootFolder, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedDocument)
                .OrderBy(path => Path.GetDirectoryName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new ToolDocumentItem(path, RootFolder))
                .ToList();

            foreach (var file in files)
                Documents.Add(file);

            SelectedDocument = Documents.FirstOrDefault();
            if (SelectedDocument == null)
                RenderedHtml = ToolDocumentationRenderer.Render(null);
        }

        [RelayCommand]
        private void OpenFolder()
        {
            Directory.CreateDirectory(RootFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = RootFolder,
                UseShellExecute = true
            });
        }

        private static bool IsSupportedDocument(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension is ".md" or ".markdown" or ".html" or ".htm" or ".txt";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
