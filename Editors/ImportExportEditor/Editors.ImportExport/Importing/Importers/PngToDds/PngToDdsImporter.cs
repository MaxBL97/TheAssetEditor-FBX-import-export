using DirectXTexNet;
using Editors.ImportExport.Importing.Importers.PngToDds.Helpers;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.Types;
using Editors.ImportExport.Common.Interfaces;
using Shared.Core.PackFiles.Models.FileSources;

namespace Editors.ImportExport.Importing.Importers.PngToDds
{

    public class PngToDdsImporter
    {
        static public PackFile Import(string inputPath, TextureType textureType, GameTypeEnum gameType, string outFileName, bool processImage = true)
        {
            var wicFlags = IsColourTexture(textureType) ? WIC_FLAGS.DEFAULT_SRGB : WIC_FLAGS.NONE;
            ScratchImage scratchImagePng = TexHelper.Instance.LoadFromWICFile(inputPath, wicFlags);

            bool isUncompressed = scratchImagePng.GetMetadata().Format == DXGI_FORMAT.B8G8R8A8_UNORM || scratchImagePng.GetMetadata().Format == DXGI_FORMAT.B8G8R8A8_UNORM_SRGB;

            var processedImage = processImage
                ? ImageProcessorFactory.CreateImageProcessor(textureType).Transform(scratchImagePng)
                : scratchImagePng.CreateImageCopy(0, false, CP_FLAGS.NONE);

            var imageWithMips = processedImage.GenerateMipMaps(TEX_FILTER_FLAGS.DEFAULT, 0);
            var ddsFormat = DDSFormatHelper.GetDDSFormat(gameType, textureType);
            var ddsImage = imageWithMips.Compress(ddsFormat, TEX_COMPRESS_FLAGS.DEFAULT, 0.5f);

            var ddsMemStream = ddsImage.SaveToDDSMemory(DDS_FLAGS.NONE);

            byte[] ddsBytes = new byte[ddsMemStream.Length];
            ddsMemStream.Read(ddsBytes, 0, ddsBytes.Length);

            var ddsPackFile = new PackFile(outFileName, new MemorySource(ddsBytes));

            return ddsPackFile;
        }

        private static bool IsColourTexture(TextureType textureType)
        {
            return textureType is TextureType.BaseColour or TextureType.Diffuse or TextureType.Specular;
        }
    }
}
