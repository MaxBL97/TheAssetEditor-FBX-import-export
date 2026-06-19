using System.IO;
using Editors.ImportExport.Misc;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Editors.ImportExport.Animation.Mirror;

public sealed class MirrorAnimXCommand(
    AnimationMirrorService mirrorService,
    IPackFileService packFileService,
    IFileSaveService fileSaveService,
    IStandardDialogs standardDialogs,
    IScopedLogger scopedLogger) : IContextMenuCommand
{
    private readonly Serilog.ILogger _logger = scopedLogger.ForContext<MirrorAnimXCommand>();
    private TreeNode _node = null!;

    public string GetDisplayName(TreeNode node) => "Create mirrored .anim (X axis)";

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

        try
        {
            var inputPath = packFileService.GetFullPath(packFile);
            var outputPath = CreateOutputPath(inputPath, AnimationMirrorAxis.X);

            _logger.Here().Information($"Creating mirrored animation '{outputPath}' from '{inputPath}'");

            var sourceAnimation = AnimationFile.Create(packFile);
            var mirroredAnimation = mirrorService.Mirror(sourceAnimation, (AnimationMirrorPlane)AnimationMirrorAxis.X);
            var bytes = AnimationFile.ConvertToBytes(mirroredAnimation);

            var savedFile = fileSaveService.Save(outputPath, bytes, prompOnConflict: true);
            if (savedFile == null)
                return;

            standardDialogs.ShowDialogBox($"Mirrored animation saved as:\n{outputPath}");
        }
        catch (Exception exception)
        {
            _logger.Here().Error($"Failed to create mirrored animation from '{packFile.Name}': {exception}");
            standardDialogs.ShowDialogBox($"Failed to create mirrored animation:\n{exception.Message}");
        }
    }

    private static string CreateOutputPath(string inputPath, AnimationMirrorAxis axis)
    {
        var directory = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        var outputFileName = $"{fileName}_mirrored_{axis.ToString().ToLowerInvariant()}{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? outputFileName
            : $"{directory}\\{outputFileName}";
    }
}
