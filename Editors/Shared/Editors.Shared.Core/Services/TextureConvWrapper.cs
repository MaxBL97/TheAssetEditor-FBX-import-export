using System.IO;
using System.Reflection;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.GameFormats.RigidModel.Types;

namespace Editors.Shared.Core.Services
{
    public class TextureConvWrapper
    {
        ILogger _logger = Logging.Create<TextureConvWrapper>();

        public TextureConvWrapper()
        {
            EnsureTexconvExists();
        }

        void EnsureTexconvExists()
        {
            var texconvPath = $"{DirectoryHelper.Applications}\\texconv.exe";

            if (!File.Exists(texconvPath))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("View3D.Content.Other.texconv.exe");
                using var fStream = new FileStream(texconvPath, FileMode.OpenOrCreate);
                stream!.CopyTo(fStream);
                _logger.Here().Information("Creating instance of texConv.exe");
            }
        }

        public void SavePNGTextureAsDDS(string pngFilePath, TextureType texureType = TextureType.Diffuse)
        {
            var texconvArguments = texureType switch
            {
                TextureType.Mask => "-f BC3_UNORM",
                TextureType.Normal => "-f BC3_UNORM",
                TextureType.Gloss => "-f BC1_UNORM",
                _ => "-f BC7_UNORM_SRGB",
            };

            var cmd = $"{texconvArguments} -y -o \"{Path.GetDirectoryName(pngFilePath)}\" \"{pngFilePath}\"";
            RunTextConv(cmd);
        }

        public void SaveDDSTextureAsPNG(string texturePath)
        {
            var outputDirectory = Path.GetDirectoryName(texturePath);
            var exitCode = RunTextConv($"-ft png -y -o \"{outputDirectory}\" \"{texturePath}\"");
            if (exitCode != 0)
            {
                _logger.Here().Warning("Default DDS to PNG conversion failed for {TexturePath}. Retrying with R8G8B8A8_UNORM output.", texturePath);
                RunTextConv($"-f R8G8B8A8_UNORM -ft png -y -o \"{outputDirectory}\" \"{texturePath}\"");
            }
        }

        int RunTextConv(string cmd)
        {
            var texconvPath = $"{DirectoryHelper.Applications}\\texconv.exe";

            using var pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = texconvPath;
            pProcess.StartInfo.Arguments = cmd;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.RedirectStandardError = true;
            pProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.Start();
            var outputTask = pProcess.StandardOutput.ReadToEndAsync();
            var errorTask = pProcess.StandardError.ReadToEndAsync();
            pProcess.WaitForExit();
            var result = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(result))
                _logger.Here().Information(result);
            if (!string.IsNullOrWhiteSpace(error))
                _logger.Here().Warning(error);
            return pProcess.ExitCode;
        }
    }


}

