using Editors.ImportExport.Exporting.Presentation.AnimToFbx;
using Editors.ImportExport.Exporting.Exporters.AnimToFbx;
using Editors.ImportExport.Importing.Presentation.FbxToRmv;
using Editors.ImportExport.Importing.Importers.FbxToRmv;
using Editors.ImportExport.Exporting.Presentation.RmvToFbx;
using Editors.ImportExport.Exporting.Exporters.RmvToFbx;
using Editors.ImportExport.Common.FbxSdk;
using Editors.ImportExport.Exporting;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Exporters.DdsToNormalPng;
using Editors.ImportExport.Exporting.Exporters.DdsToPng;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using Editors.ImportExport.Exporting.Presentation;
using Editors.ImportExport.Exporting.Presentation.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Presentation.DdsToNormalPng;
using Editors.ImportExport.Exporting.Presentation.DdsToPng;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.ContextMenu;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Importing.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Editors.ImportImport.Importing.Presentation.RmvToGltf;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;

namespace Editors.ImportExport
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection services)
        {
            services.AddSingleton<AutodeskFbxService>();

            // Exporter ViewModels
            RegisterWindow<ExportWindow>(services);
            services.AddTransient<ExporterCoreViewModel>();
            services.AddTransient<IExporterViewModel, DdsToPngExporterViewModel>();
            services.AddTransient<IExporterViewModel, DdsToMaterialPngViewModel>();
            services.AddTransient<IExporterViewModel, DdsToNormalPngViewModel>();
            services.AddTransient<IExporterViewModel, RmvToGltfExporterViewModel>();
            services.AddTransient<IExporterViewModel, RmvToGltfStaticExporterViewModel>();
            services.AddTransient<IExporterViewModel, RmvToFbxExporterViewModel>();
            services.AddTransient<IExporterViewModel, AnimToFbxExporterViewModel>();

            // Exporters
            services.AddTransient<IDdsToMaterialPngExporter, DdsToMaterialPngExporter>();
            services.AddTransient<DdsToPngExporter>();
            services.AddTransient<IDdsToNormalPngExporter, DdsToNormalPngExporter>();
            services.AddTransient<RmvToGltfExporter>();
            services.AddTransient<RmvToGltfStaticExporter>();
            services.AddTransient<RmvToFbxExporter>();
            services.AddTransient<AnimToFbxExporter>();

            // Importer ViewModels
            RegisterWindow<ImportWindow>(services);
            services.AddTransient<ImporterCoreViewModel>();
            services.AddTransient<IImporterViewModel, RmvToGltfImporterViewModel>();
            services.AddTransient<IImporterViewModel, FbxToRmvImporterViewModel>();

            // Importers
            services.AddTransient<GltfImporter>();
            services.AddTransient<FbxImporter>();
            services.AddTransient<RmvMaterialBuilder>();

            // Image Save Helper
            services.AddScoped<IImageSaveHandler, SystemImageSaveHandler>();

            // Helpers to ensure we can hook up to the UI
            services.AddTransient<AdvancedExportCommand>();
            services.AddTransient<AdvancedImportCommand>();
            services.AddTransient<IExportFileContextMenuHelper, ExportFileContextMenuHelper>();
            services.AddTransient<DisplayExportFileToolCommand>();

            services.AddTransient<IImportFileContextMenuHelper, ImportFileContextMenuHelper>();
            services.AddTransient<DisplayImportFileToolCommand>();

            services.AddTransient<GltfMeshBuilder>();
            services.AddTransient<GltfStaticMeshBuilder>();
            services.AddTransient<IGltfTextureHandler, GltfTextureHandler>();
            services.AddTransient<IGltfSceneSaver, GltfSceneSaver>();
            services.AddTransient<IGltfSceneLoader, GltfSceneLoader>();
            services.AddTransient<GltfSkeletonBuilder>();
            services.AddTransient<GltfAnimationBuilder>();

            services.AddSingleton<IPackFileContextMenuRegistration, ImportExportPackFileContextMenuRegistration>();

            RegisterAllAsInterface<IDeveloperConfiguration>(services, ServiceLifetime.Transient);
        }
    }

    public class ImportExportPackFileContextMenuRegistration : IPackFileContextMenuRegistration
    {
        public void Register(PackFileContextMenuRegistry registry)
        {
            registry.RegisterPackFileContextMenuItem<AdvancedExportCommand>(ContextMenuType.MainApplication, path: "Export", priority: 10, ContextMenuCluster.Export);
            registry.RegisterPackFileContextMenuItem<AdvancedImportCommand>(ContextMenuType.MainApplication, path: "Import", priority: 20, ContextMenuCluster.FolderOperation);
        }
    }
}
