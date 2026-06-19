using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Shared.Core.Misc;
using Shared.Core.ToolCreation;
using WinForms = System.Windows.Forms;

namespace Editors.TextureEditor.TextureTools
{
    public sealed class TextureToolsViewModel : NotifyPropertyChangedImpl, IEditorInterface
    {
        private readonly TextureToolsService _service;
        private string _texconvPath = @"C:\Dev\TexConv\texconv.exe";
        private string _inputPath = string.Empty;
        private string _outputFolderName = string.Empty;
        private bool _recursive = true;
        private bool _overwrite = true;
        private bool _outputBesideInput;
        private TextureToolKind _textureKind = TextureToolKind.Auto;
        private bool _convertBlueNormalToTwOrangeNormal = true;
        private bool _adjustTwNormalChannelsForMirror = true;
        private int _rotationDegrees;
        private bool _mirrorX;
        private bool _mirrorY;
        private string _renameOldText = string.Empty;
        private string _renameNewText = string.Empty;
        private string _deleteExtension = "png";
        private bool _deleteNormals = true;
        private bool _deleteOtherMaps = true;
        private bool _dryRun = true;
        private bool _isBusy;

        public string DisplayName { get; set; } = "Texture/File Tools";

        public string TexconvPath { get => _texconvPath; set => SetAndNotify(ref _texconvPath, value); }
        public string InputPath { get => _inputPath; set => SetAndNotify(ref _inputPath, value); }
        public string OutputFolderName { get => _outputFolderName; set => SetAndNotify(ref _outputFolderName, value); }
        public bool Recursive { get => _recursive; set => SetAndNotify(ref _recursive, value); }
        public bool Overwrite { get => _overwrite; set => SetAndNotify(ref _overwrite, value); }
        public bool OutputBesideInput { get => _outputBesideInput; set => SetAndNotify(ref _outputBesideInput, value); }
        public TextureToolKind TextureKind { get => _textureKind; set => SetAndNotify(ref _textureKind, value); }
        public bool ConvertBlueNormalToTwOrangeNormal { get => _convertBlueNormalToTwOrangeNormal; set => SetAndNotify(ref _convertBlueNormalToTwOrangeNormal, value); }
        public bool AdjustTwNormalChannelsForMirror { get => _adjustTwNormalChannelsForMirror; set => SetAndNotify(ref _adjustTwNormalChannelsForMirror, value); }
        public int RotationDegrees { get => _rotationDegrees; set => SetAndNotify(ref _rotationDegrees, value); }
        public bool MirrorX { get => _mirrorX; set => SetAndNotify(ref _mirrorX, value); }
        public bool MirrorY { get => _mirrorY; set => SetAndNotify(ref _mirrorY, value); }
        public string RenameOldText { get => _renameOldText; set => SetAndNotify(ref _renameOldText, value); }
        public string RenameNewText { get => _renameNewText; set => SetAndNotify(ref _renameNewText, value); }
        public string DeleteExtension { get => _deleteExtension; set => SetAndNotify(ref _deleteExtension, value); }
        public bool DeleteNormals { get => _deleteNormals; set => SetAndNotify(ref _deleteNormals, value); }
        public bool DeleteOtherMaps { get => _deleteOtherMaps; set => SetAndNotify(ref _deleteOtherMaps, value); }
        public bool DryRun { get => _dryRun; set => SetAndNotify(ref _dryRun, value); }
        public bool IsBusy { get => _isBusy; set { SetAndNotify(ref _isBusy, value); RefreshCommands(); } }

        public Array TextureKinds { get; } = Enum.GetValues(typeof(TextureToolKind));
        public ObservableCollection<string> LogLines { get; } = [];

        public ICommand BrowseTexconvCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand ConvertDdsToPngCommand { get; }
        public ICommand ConvertPngToDdsCommand { get; }
        public ICommand TransformDdsCommand { get; }
        public ICommand RenameFilesCommand { get; }
        public ICommand DeleteFilesCommand { get; }
        public ICommand CopyBlenderRemoveVertexGroupsScriptCommand { get; }
        public ICommand ClearLogCommand { get; }

        public TextureToolsViewModel(TextureToolsService service)
        {
            _service = service;
            BrowseTexconvCommand = new DelegateCommand(_ => BrowseTexconv(), _ => !IsBusy);
            BrowseFileCommand = new DelegateCommand(_ => BrowseFile(), _ => !IsBusy);
            BrowseFolderCommand = new DelegateCommand(_ => BrowseFolder(), _ => !IsBusy);
            ConvertDdsToPngCommand = new DelegateCommand(async _ => await RunAsync(() => _service.ConvertDdsToPng(CreateOptions())), _ => !IsBusy);
            ConvertPngToDdsCommand = new DelegateCommand(async _ => await RunAsync(() => _service.ConvertPngToDds(CreateOptions())), _ => !IsBusy);
            TransformDdsCommand = new DelegateCommand(async _ => await RunAsync(() => _service.TransformDds(CreateOptions())), _ => !IsBusy);
            RenameFilesCommand = new DelegateCommand(async _ => await RunAsync(() => _service.RenameFiles(InputPath, RenameOldText, RenameNewText, Recursive, DryRun)), _ => !IsBusy);
            DeleteFilesCommand = new DelegateCommand(async _ => await RunAsync(() => _service.DeleteFiles(InputPath, DeleteExtension, DeleteNormals, DeleteOtherMaps, Recursive, DryRun)), _ => !IsBusy);
            CopyBlenderRemoveVertexGroupsScriptCommand = new DelegateCommand(_ => Clipboard.SetText(BlenderRemoveVertexGroupsScript), _ => !IsBusy);
            ClearLogCommand = new DelegateCommand(_ => LogLines.Clear(), _ => !IsBusy);
        }

        public void Close()
        {
        }

        private TextureToolOptions CreateOptions() => new(
            TexconvPath,
            InputPath,
            OutputFolderName,
            Recursive,
            Overwrite,
            OutputBesideInput,
            TextureKind,
            ConvertBlueNormalToTwOrangeNormal,
            AdjustTwNormalChannelsForMirror,
            RotationDegrees,
            MirrorX,
            MirrorY);

        private async Task RunAsync(Func<TextureToolRunResult> action)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                var result = await Task.Run(action);
                LogLines.Add($"Processed: {result.ProcessedCount}, warnings: {result.WarningCount}, errors: {result.ErrorCount}");
                foreach (var line in result.LogLines)
                    LogLines.Add(line);
            }
            catch (Exception ex)
            {
                LogLines.Add("ERROR: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void BrowseTexconv()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "texconv.exe|texconv.exe|Executables|*.exe|All files|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == true)
                TexconvPath = dialog.FileName;
        }

        private void BrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Texture files|*.dds;*.png|DDS files|*.dds|PNG files|*.png|All files|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == true)
                InputPath = dialog.FileName;
        }

        private void BrowseFolder()
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select a texture or file-processing folder",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                InputPath = dialog.SelectedPath;
        }

        private void RefreshCommands()
        {
            (BrowseTexconvCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (BrowseFileCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (BrowseFolderCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ConvertDdsToPngCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ConvertPngToDdsCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (TransformDdsCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (RenameFilesCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (DeleteFilesCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (CopyBlenderRemoveVertexGroupsScriptCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ClearLogCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }

        private const string BlenderRemoveVertexGroupsScript = """
import bpy
selection = bpy.context.selected_objects
for ob in selection:
    if ob.type == 'MESH':
        for group in list(ob.vertex_groups):
            ob.vertex_groups.remove(group)
""";

        private sealed class DelegateCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;

            public DelegateCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged;
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
