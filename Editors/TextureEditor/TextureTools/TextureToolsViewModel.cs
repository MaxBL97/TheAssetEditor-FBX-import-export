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
        private bool _convertTwOrangeNormalToBlueNormal = false;
        private bool _convertMaterialMapChannels = true;
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
        public string OutputFolderName { get => _outputFolderName; set { SetAndNotify(ref _outputFolderName, value); NotifyGuidanceChanged(); } }
        public bool Recursive { get => _recursive; set { SetAndNotify(ref _recursive, value); NotifyGuidanceChanged(); } }
        public bool Overwrite { get => _overwrite; set { SetAndNotify(ref _overwrite, value); NotifyGuidanceChanged(); } }
        public bool OutputBesideInput { get => _outputBesideInput; set { SetAndNotify(ref _outputBesideInput, value); NotifyGuidanceChanged(); } }
        public TextureToolKind TextureKind { get => _textureKind; set { SetAndNotify(ref _textureKind, value); NotifyGuidanceChanged(); } }
        public bool ConvertBlueNormalToTwOrangeNormal { get => _convertBlueNormalToTwOrangeNormal; set { SetAndNotify(ref _convertBlueNormalToTwOrangeNormal, value); NotifyGuidanceChanged(); } }
        public bool ConvertTwOrangeNormalToBlueNormal { get => _convertTwOrangeNormalToBlueNormal; set { SetAndNotify(ref _convertTwOrangeNormalToBlueNormal, value); NotifyGuidanceChanged(); } }
        public bool ConvertMaterialMapChannels { get => _convertMaterialMapChannels; set { SetAndNotify(ref _convertMaterialMapChannels, value); NotifyGuidanceChanged(); } }
        public bool AdjustTwNormalChannelsForMirror { get => _adjustTwNormalChannelsForMirror; set { SetAndNotify(ref _adjustTwNormalChannelsForMirror, value); NotifyGuidanceChanged(); } }
        public int RotationDegrees { get => _rotationDegrees; set { SetAndNotify(ref _rotationDegrees, value); NotifyGuidanceChanged(); } }
        public bool MirrorX { get => _mirrorX; set { SetAndNotify(ref _mirrorX, value); NotifyGuidanceChanged(); } }
        public bool MirrorY { get => _mirrorY; set { SetAndNotify(ref _mirrorY, value); NotifyGuidanceChanged(); } }
        public string RenameOldText { get => _renameOldText; set => SetAndNotify(ref _renameOldText, value); }
        public string RenameNewText { get => _renameNewText; set => SetAndNotify(ref _renameNewText, value); }
        public string DeleteExtension { get => _deleteExtension; set => SetAndNotify(ref _deleteExtension, value); }
        public bool DeleteNormals { get => _deleteNormals; set => SetAndNotify(ref _deleteNormals, value); }
        public bool DeleteOtherMaps { get => _deleteOtherMaps; set => SetAndNotify(ref _deleteOtherMaps, value); }
        public bool DryRun { get => _dryRun; set { SetAndNotify(ref _dryRun, value); NotifyGuidanceChanged(); } }
        public bool IsBusy { get => _isBusy; set { SetAndNotify(ref _isBusy, value); RefreshCommands(); } }

        public Array TextureKinds { get; } = Enum.GetValues(typeof(TextureToolKind));
        public ObservableCollection<string> LogLines { get; } = [];


        public string OutputLocationDescription => OutputBesideInput
            ? "Output beside input is ON: converted files overwrite/write next to the source files when overwrite is enabled."
            : $"Output beside input is OFF: converted files go to '{ResolvedOutputFolderPreview}'.";

        public string ResolvedOutputFolderPreview => string.IsNullOrWhiteSpace(OutputFolderName)
            ? "ConvDDS / ConvPNG / TransformedDDS depending on the tab"
            : OutputFolderName.Trim();

        public string TextureKindDescription => TextureKind switch
        {
            TextureToolKind.Auto => "Auto detects by suffix: _base_colour/_basecolor, _normal/_n, _material_map, _mask. Unknown names are treated as BaseColour, so select the kind manually for unsafe names.",
            TextureToolKind.BaseColour => "BaseColour: BC1_UNORM_SRGB with -srgb. Use for albedo/diffuse/base_colour only.",
            TextureToolKind.Normal => "Normal: BC3_UNORM linear. PNG->DDS can repack standard blue/purple normals to TW orange. DDS->PNG can export TW orange normals to Blender blue preview normals.",
            TextureToolKind.MaterialMap => "MaterialMap: BC1_UNORM linear. Optional R/B channel swap is available. This tool does not combine specular + gloss yet.",
            TextureToolKind.Mask => "Mask: BC1_UNORM linear. Use for WH3 mask maps unless a specific older-game workflow needs another format.",
            TextureToolKind.GenericLinear => "GenericLinear: BC1_UNORM linear. Use for data maps that must not receive sRGB conversion.",
            TextureToolKind.GenericSrgb => "GenericSrgb: BC1_UNORM_SRGB with -srgb. Use for ordinary colour textures.",
            _ => string.Empty
        };

        public string NormalConversionDescription => ConvertBlueNormalToTwOrangeNormal
            ? "PNG -> DDS normal conversion ON: blue/purple normals are repacked to TW orange as R=255, G=G, B=0, A=R before DDS compression."
            : "PNG -> DDS normal conversion OFF: normal pixels are not repacked; use only when the source is already TW-orange packed or when you only want format conversion.";

        public string DdsNormalConversionDescription => ConvertTwOrangeNormalToBlueNormal
            ? "DDS -> PNG normal conversion ON: TW-orange normals are exported as Blender/glTF-style blue normals using the same rule as FBX export."
            : "DDS -> PNG normal conversion OFF: DDS -> PNG only changes the file format and keeps channels as texconv outputs them.";

        public string MaterialMapConversionDescription => ConvertMaterialMapChannels
            ? "Material map channel swap ON: R/B channels are swapped before output. Use for Blender/glTF-like material maps that need conversion to or from CA/WH3 layout."
            : "Material map channel swap OFF: channels are preserved. Use for format-only conversion or already CA/WH3 material maps.";

        public string TransformWarningDescription
        {
            get
            {
                var hasTransform = RotationDegrees != 0 || MirrorX || MirrorY;
                if (!hasTransform)
                    return "No image transform is currently selected.";

                if (TextureKind == TextureToolKind.Normal || TextureKind == TextureToolKind.Auto)
                    return AdjustTwNormalChannelsForMirror
                        ? "Mirror/rotate is selected. For TW-orange normal maps, mirror channel adjustment is ON; still review the result visually."
                        : "Mirror/rotate is selected. Normal-channel mirror adjustment is OFF; mirrored normal maps may shade incorrectly.";

                return "Mirror/rotate is selected. This is safe for colour maps, but review data maps after conversion.";
            }
        }

        public string DeleteSafetyDescription => DryRun
            ? "Dry run is ON: delete/rename actions only list what would happen."
            : "Dry run is OFF: delete/rename actions will modify files immediately.";

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
            ConvertTwOrangeNormalToBlueNormal,
            ConvertMaterialMapChannels,
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


        private void NotifyGuidanceChanged()
        {
            NotifyPropertyChanged(nameof(OutputLocationDescription));
            NotifyPropertyChanged(nameof(ResolvedOutputFolderPreview));
            NotifyPropertyChanged(nameof(TextureKindDescription));
            NotifyPropertyChanged(nameof(NormalConversionDescription));
            NotifyPropertyChanged(nameof(DdsNormalConversionDescription));
            NotifyPropertyChanged(nameof(MaterialMapConversionDescription));
            NotifyPropertyChanged(nameof(TransformWarningDescription));
            NotifyPropertyChanged(nameof(DeleteSafetyDescription));
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
