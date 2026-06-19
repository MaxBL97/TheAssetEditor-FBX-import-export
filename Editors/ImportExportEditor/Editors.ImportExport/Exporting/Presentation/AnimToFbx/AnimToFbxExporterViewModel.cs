using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.AnimToFbx;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Exporting.Presentation.AnimToFbx
{
    internal partial class AnimToFbxExporterViewModel : ObservableObject, IExporterViewModel, IViewProvider<AnimToFbxExporterView>
    {
        private readonly AnimToFbxExporter _exporter;

        public string DisplayName => "ANIM to FBX";
        public string OutputExtension => ".fbx";

        [ObservableProperty] bool _blenderFriendlyOrientation = false;

        public AnimToFbxExporterViewModel(AnimToFbxExporter exporter)
        {
            _exporter = exporter;
        }

        public ExportSupportEnum CanExportFile(PackFile file) => _exporter.CanExportFile(file);

        public void Execute(PackFile exportSource, string outputPath, bool generateImporter)
        {
            _exporter.Export(exportSource, outputPath, BlenderFriendlyOrientation);
        }
    }
}
