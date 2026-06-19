using System.IO;
using System.Windows;
using Editors.ImportExport.Misc;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Editors.ImportExport.Animation.Mirror;

public sealed class MirrorAnimCommand(
    AnimationMirrorService mirrorService,
    IPackFileService packFileService,
    IFileSaveService fileSaveService,
    IStandardDialogs standardDialogs,
    IScopedLogger scopedLogger) : IContextMenuCommand
{
    private readonly Serilog.ILogger _logger = scopedLogger.ForContext<MirrorAnimCommand>();
    private TreeNode _node = null!;

    public string GetDisplayName(TreeNode node) => "Create plane-mirrored .anim...";

    public bool ShouldAdd(TreeNode node)
    {
        var packFile = TreeNodeHelper.GetPackFile(node);
        return node.NodeType == NodeType.File && packFile != null && FileExtensionHelper.IsAnimFile(packFile.Name);
    }

    public bool IsEnabled(TreeNode node) => TreeNodeHelper.GetPackFile(node) != null;

    public void Configure(TreeNode node)
    {
        _node = node;
    }

    public void Execute()
    {
        var packFile = TreeNodeHelper.GetPackFile(_node);
        if (packFile == null)
            return;

        var optionsWindow = new MirrorAnimOptionsWindow
        {
            Owner = Application.Current?.MainWindow
        };

        if (optionsWindow.ShowDialog() != true)
            return;

        var plane = optionsWindow.SelectedPlane;

        try
        {
            var inputPath = packFileService.GetFullPath(packFile);
            var outputPath = CreateOutputPath(inputPath, plane);

            _logger.Here().Information($"Creating plane-mirrored animation '{outputPath}' from '{inputPath}' with plane '{plane}'");

            var sourceAnimation = AnimationFile.Create(packFile);
            var mirroredAnimation = mirrorService.Mirror(sourceAnimation, plane);
            var bytes = AnimationFile.ConvertToBytes(mirroredAnimation);

            var savedFile = fileSaveService.Save(outputPath, bytes, prompOnConflict: true);
            if (savedFile == null)
                return;

            standardDialogs.ShowDialogBox($"Plane-mirrored animation saved as:\n{outputPath}", "Mirror animation");
        }
        catch (Exception exception)
        {
            _logger.Here().Error($"Failed to create plane-mirrored animation from '{packFile.Name}': {exception}");
            standardDialogs.ShowDialogBox($"Failed to create plane-mirrored animation:\n{exception.Message}", "Mirror animation");
        }
    }

    private static string CreateOutputPath(string inputPath, AnimationMirrorPlane plane)
    {
        var directory = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        var outputFileName = $"{fileName}_mirrored_{CreatePlaneSuffix(plane)}{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? outputFileName
            : $"{directory}\\{outputFileName}";
    }

    private static string CreatePlaneSuffix(AnimationMirrorPlane plane)
    {
        return plane switch
        {
            AnimationMirrorPlane.YZ => "plane_yz_invert_x",
            AnimationMirrorPlane.XZ => "plane_xz_invert_y",
            AnimationMirrorPlane.XY => "plane_xy_invert_z",
            _ => "plane_unknown"
        };
    }
}
