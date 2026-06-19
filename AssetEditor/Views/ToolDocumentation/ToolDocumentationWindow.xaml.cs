using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using AssetEditor.ViewModels.ToolDocumentation;

namespace AssetEditor.Views.ToolDocumentation
{
    public partial class ToolDocumentationWindow : Window
    {
        private readonly ToolDocumentationViewModel _viewModel;
        private bool _browserInitialized;

        public ToolDocumentationWindow()
        {
            InitializeComponent();
            _viewModel = new ToolDocumentationViewModel();
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            DataContext = _viewModel;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await InitializeBrowserAsync();
            NavigateToRenderedHtml();
        }

        private async Task InitializeBrowserAsync()
        {
            if (_browserInitialized)
                return;

            await DocumentBrowser.EnsureCoreWebView2Async();
            _browserInitialized = true;
        }

        private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ToolDocumentationViewModel.RenderedHtml))
                return;

            await InitializeBrowserAsync();
            NavigateToRenderedHtml();
        }

        private void NavigateToRenderedHtml()
        {
            if (!_browserInitialized)
                return;

            DocumentBrowser.NavigateToString(_viewModel.RenderedHtml);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            DocumentBrowser.Dispose();
        }
    }
}
