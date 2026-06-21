using System.Collections.Generic;

namespace Editors.TextureEditor.TextureTools
{
    public enum TextureToolKind
    {
        Auto,
        BaseColour,
        Normal,
        MaterialMap,
        Mask,
        GenericLinear,
        GenericSrgb
    }

    public sealed record TextureToolOptions(
        string TexconvPath,
        string InputPath,
        string OutputFolderName,
        bool Recursive,
        bool Overwrite,
        bool OutputBesideInput,
        TextureToolKind TextureKind,
        bool ConvertBlueNormalToTwOrangeNormal,
        bool ConvertTwOrangeNormalToBlueNormal,
        bool ConvertMaterialMapChannels,
        bool AdjustTwNormalChannelsForMirror,
        int RotationDegrees,
        bool MirrorX,
        bool MirrorY);

    public sealed record MaterialMapBuildOptions(
        string TexconvPath,
        string SpecularInputPath,
        string GlossInputPath,
        string OutputFolderName,
        bool Recursive,
        bool Overwrite,
        bool OutputBesideInput,
        bool InvertGlossToRoughness,
        int DefaultMetalness,
        int DefaultRoughness);

    public sealed record TextureToolRunResult(int ProcessedCount, int WarningCount, int ErrorCount, IReadOnlyList<string> LogLines);
}
