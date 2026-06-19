using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Editors.TextureEditor.TextureTools
{
    public sealed class TextureToolsService
    {
        private const string DefaultOutputDdsFolder = "ConvDDS";
        private const string DefaultOutputPngFolder = "ConvPNG";

        public TextureToolRunResult ConvertDdsToPng(TextureToolOptions options)
        {
            var log = new List<string>();
            var files = EnumerateInputFiles(options.InputPath, ".dds", options.Recursive).ToList();
            if (files.Count == 0)
                return WarnOnly(log, "No DDS files found.");

            ValidateTexconv(options.TexconvPath);

            var processed = 0;
            var errors = 0;

            foreach (var file in files)
            {
                try
                {
                    var output = ResolveOutputDirectory(file, options.OutputBesideInput, options.OutputFolderName, DefaultOutputPngFolder);
                    Directory.CreateDirectory(output);
                    RunTexconv(options.TexconvPath, ["-y", "-ft", "png", "-o", output, file], log);
                    processed++;
                    log.Add($"DDS -> PNG: {file}");
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {file}: {ex.Message}");
                }
            }

            return new TextureToolRunResult(processed, 0, errors, log);
        }

        public TextureToolRunResult ConvertPngToDds(TextureToolOptions options)
        {
            var log = new List<string>();
            var files = EnumerateInputFiles(options.InputPath, ".png", options.Recursive).ToList();
            if (files.Count == 0)
                return WarnOnly(log, "No PNG files found.");

            ValidateTexconv(options.TexconvPath);

            var processed = 0;
            var warnings = 0;
            var errors = 0;

            foreach (var file in files)
            {
                try
                {
                    var kind = ResolveTextureKind(file, options.TextureKind);
                    var output = ResolveOutputDirectory(file, options.OutputBesideInput, options.OutputFolderName, DefaultOutputDdsFolder);
                    Directory.CreateDirectory(output);

                    var inputForTexconv = file;
                    string? temporaryPng = null;

                    var needsPixelTransform = options.ConvertBlueNormalToTwOrangeNormal && kind == TextureToolKind.Normal;
                    var needsGeometryTransform = options.RotationDegrees != 0 || options.MirrorX || options.MirrorY;

                    if (needsPixelTransform || needsGeometryTransform)
                    {
                        temporaryPng = Path.Combine(output, $"__ae_texture_tool_{Guid.NewGuid():N}.png");
                        TransformPng(file, temporaryPng, options.RotationDegrees, options.MirrorX, options.MirrorY, needsPixelTransform, options.AdjustTwNormalChannelsForMirror && kind == TextureToolKind.Normal);
                        inputForTexconv = temporaryPng;
                    }

                    RunTexconv(options.TexconvPath, BuildPngToDdsArguments(inputForTexconv, output, kind, options.Overwrite), log);
                    MoveTexconvOutputToExpectedName(inputForTexconv, file, output, ".dds", options.Overwrite);

                    if (temporaryPng != null && File.Exists(temporaryPng))
                        File.Delete(temporaryPng);

                    processed++;
                    log.Add($"PNG -> DDS ({kind}): {file}");
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {file}: {ex.Message}");
                }
            }

            return new TextureToolRunResult(processed, warnings, errors, log);
        }

        public TextureToolRunResult TransformDds(TextureToolOptions options)
        {
            var log = new List<string>();
            var files = EnumerateInputFiles(options.InputPath, ".dds", options.Recursive).ToList();
            if (files.Count == 0)
                return WarnOnly(log, "No DDS files found.");

            ValidateTexconv(options.TexconvPath);

            var processed = 0;
            var errors = 0;
            var warnings = 0;

            foreach (var file in files)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "AssetEditor_TextureTools", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    RunTexconv(options.TexconvPath, ["-y", "-ft", "png", "-o", tempDir, file], log);
                    var tempPng = Directory.GetFiles(tempDir, "*.png").FirstOrDefault();
                    if (tempPng == null)
                        throw new InvalidOperationException("texconv did not generate a temporary PNG.");

                    var transformed = Path.Combine(tempDir, "__ae_transformed.png");
                    var kind = ResolveTextureKind(file, options.TextureKind);
                    TransformPng(tempPng, transformed, options.RotationDegrees, options.MirrorX, options.MirrorY, false, options.AdjustTwNormalChannelsForMirror && kind == TextureToolKind.Normal);

                    var output = options.OutputBesideInput
                        ? Path.GetDirectoryName(file)!
                        : ResolveOutputDirectory(file, false, options.OutputFolderName, "TransformedDDS");
                    Directory.CreateDirectory(output);

                    RunTexconv(options.TexconvPath, BuildPngToDdsArguments(transformed, output, kind, options.Overwrite), log);
                    MoveTexconvOutputToExpectedName(transformed, file, output, ".dds", options.Overwrite);

                    if (kind == TextureToolKind.Normal && options.RotationDegrees != 0)
                    {
                        warnings++;
                        log.Add("WARNING: rotated normal maps should be reviewed. Mirrored TW-orange normal channels are adjusted, but arbitrary normal rotation is still visually sensitive.");
                    }

                    processed++;
                    log.Add($"DDS transformed ({kind}): {file}");
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {file}: {ex.Message}");
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }

            return new TextureToolRunResult(processed, warnings, errors, log);
        }

        public TextureToolRunResult RenameFiles(string inputPath, string oldText, string newText, bool recursive, bool dryRun)
        {
            var log = new List<string>();
            if (string.IsNullOrWhiteSpace(oldText))
                return WarnOnly(log, "Text to replace is empty.");

            var files = EnumerateInputFiles(inputPath, null, recursive).ToList();
            var processed = 0;
            var errors = 0;

            foreach (var file in files)
            {
                try
                {
                    var directory = Path.GetDirectoryName(file)!;
                    var name = Path.GetFileName(file);
                    var newName = name.Replace(oldText, newText, StringComparison.Ordinal);
                    if (newName == name)
                        continue;

                    var target = Path.Combine(directory, newName);
                    if (File.Exists(target))
                        throw new IOException($"Target already exists: {target}");

                    log.Add(dryRun ? $"DRY RUN: {name} -> {newName}" : $"Renamed: {name} -> {newName}");
                    if (!dryRun)
                        File.Move(file, target);
                    processed++;
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {file}: {ex.Message}");
                }
            }

            return new TextureToolRunResult(processed, 0, errors, log);
        }

        public TextureToolRunResult DeleteFiles(string inputPath, string extension, bool includeNormals, bool includeOthers, bool recursive, bool dryRun)
        {
            var log = new List<string>();
            if (!includeNormals && !includeOthers)
                return WarnOnly(log, "Neither normal maps nor other maps are selected.");

            extension = extension.Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension))
                return WarnOnly(log, "Extension is empty.");

            var files = EnumerateInputFiles(inputPath, "." + extension, recursive)
                .Where(path =>
                {
                    var isNormal = IsNormalMap(path);
                    return (isNormal && includeNormals) || (!isNormal && includeOthers);
                })
                .ToList();

            var processed = 0;
            var errors = 0;

            foreach (var file in files)
            {
                try
                {
                    log.Add(dryRun ? $"DRY RUN delete: {file}" : $"Deleted: {file}");
                    if (!dryRun)
                        File.Delete(file);
                    processed++;
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {file}: {ex.Message}");
                }
            }

            return new TextureToolRunResult(processed, 0, errors, log);
        }

        private static TextureToolRunResult WarnOnly(List<string> log, string message)
        {
            log.Add("WARNING: " + message);
            return new TextureToolRunResult(0, 1, 0, log);
        }

        private static IEnumerable<string> EnumerateInputFiles(string inputPath, string? extension, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                yield break;

            if (File.Exists(inputPath))
            {
                if (extension == null || inputPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    yield return inputPath;
                yield break;
            }

            if (!Directory.Exists(inputPath))
                yield break;

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(inputPath, "*", option))
            {
                if (extension == null || file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }
        }

        private static void ValidateTexconv(string texconvPath)
        {
            if (string.IsNullOrWhiteSpace(texconvPath) || !File.Exists(texconvPath))
                throw new FileNotFoundException("texconv.exe was not found. Set the TexConv path first.", texconvPath);
        }

        private static string ResolveOutputDirectory(string inputFile, bool besideInput, string configuredFolderName, string defaultFolderName)
        {
            var parent = Path.GetDirectoryName(inputFile)!;
            if (besideInput)
                return parent;

            var folderName = string.IsNullOrWhiteSpace(configuredFolderName) ? defaultFolderName : configuredFolderName.Trim();
            return Path.Combine(parent, folderName);
        }

        private static TextureToolKind ResolveTextureKind(string path, TextureToolKind requested)
        {
            if (requested != TextureToolKind.Auto)
                return requested;

            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (IsNormalMap(path))
                return TextureToolKind.Normal;
            if (name.EndsWith("_material_map") || name.EndsWith("_mat_map") || name.EndsWith("_mat"))
                return TextureToolKind.MaterialMap;
            if (name.EndsWith("_mask") || name.EndsWith("_msk"))
                return TextureToolKind.Mask;
            if (name.EndsWith("_base_colour") || name.EndsWith("_basecolor") || name.EndsWith("_bc") || name.EndsWith("_diffuse") || name.EndsWith("_d"))
                return TextureToolKind.BaseColour;

            return TextureToolKind.BaseColour;
        }

        private static bool IsNormalMap(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return name.EndsWith("_n") || name.EndsWith("_normal") || name.EndsWith("_normal_map");
        }

        private static IReadOnlyList<string> BuildPngToDdsArguments(string inputFile, string outputFolder, TextureToolKind kind, bool overwrite)
        {
            var args = new List<string>();
            if (overwrite)
                args.Add("-y");

            args.AddRange(["-f", GetTexconvFormat(kind), "-m", "0", "-if", "CUBIC", "-dx10"]);
            if (UsesSrgb(kind))
                args.Add("-srgb");
            args.AddRange(["-o", outputFolder, inputFile]);
            return args;
        }

        private static string GetTexconvFormat(TextureToolKind kind) => kind switch
        {
            TextureToolKind.Normal => "BC3_UNORM",
            TextureToolKind.MaterialMap => "BC1_UNORM",
            TextureToolKind.Mask => "BC1_UNORM",
            TextureToolKind.GenericLinear => "BC1_UNORM",
            TextureToolKind.GenericSrgb => "BC1_UNORM_SRGB",
            _ => "BC1_UNORM_SRGB"
        };

        private static bool UsesSrgb(TextureToolKind kind) => kind is TextureToolKind.BaseColour or TextureToolKind.GenericSrgb;

        private static void RunTexconv(string texconvPath, IReadOnlyList<string> arguments, List<string> log)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = texconvPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start texconv.exe.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout))
                log.Add(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr))
                log.Add(stderr.Trim());

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"texconv failed with exit code {process.ExitCode}.");
        }

        private static void MoveTexconvOutputToExpectedName(string inputForTexconv, string originalInput, string outputFolder, string outputExtension, bool overwrite)
        {
            var produced = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(inputForTexconv) + outputExtension);
            var expected = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(originalInput) + outputExtension);

            if (string.Equals(produced, expected, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(expected))
            {
                if (!overwrite)
                    throw new IOException($"Output already exists: {expected}");
                File.Delete(expected);
            }

            if (File.Exists(produced))
                File.Move(produced, expected);
        }

        private static void TransformPng(string input, string output, int rotationDegrees, bool mirrorX, bool mirrorY, bool blueNormalToTwOrange, bool adjustTwNormalForMirror)
        {
            var bitmap = LoadBitmap(input);
            var pixels = CopyPixels(bitmap, out var width, out var height);

            if (blueNormalToTwOrange)
                ConvertBlueNormalToTwOrange(pixels);

            if (adjustTwNormalForMirror)
                AdjustTwOrangeNormalForMirror(pixels, mirrorX, mirrorY);

            if (mirrorX)
                pixels = MirrorHorizontal(pixels, width, height);
            if (mirrorY)
                pixels = MirrorVertical(pixels, width, height);

            var normalizedRotation = NormalizeRotation(rotationDegrees);
            pixels = normalizedRotation switch
            {
                0 => pixels,
                90 => Rotate90CounterClockwise(pixels, width, height, out width, out height),
                180 => Rotate180(pixels, width, height),
                270 => Rotate90Clockwise(pixels, width, height, out width, out height),
                _ => throw new NotSupportedException("Only 0, 90, 180 and 270 degree rotations are supported by the integrated tool.")
            };

            SavePng(output, pixels, width, height);
        }

        private static int NormalizeRotation(int rotationDegrees)
        {
            var value = rotationDegrees % 360;
            if (value < 0)
                value += 360;
            return value;
        }

        private static byte[] CopyPixels(BitmapSource bitmap, out int width, out int height)
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        private static BitmapSource LoadBitmap(string path)
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }

        private static void SavePng(string path, byte[] pixels, int width, int height)
        {
            var stride = width * 4;
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void ConvertBlueNormalToTwOrange(byte[] pixels)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var oldR = pixels[i + 2];
                var oldG = pixels[i + 1];
                pixels[i + 0] = 0;      // B
                pixels[i + 1] = oldG;   // G
                pixels[i + 2] = 255;    // R
                pixels[i + 3] = oldR;   // A stores original red/X
            }
        }

        private static void AdjustTwOrangeNormalForMirror(byte[] pixels, bool mirrorX, bool mirrorY)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (mirrorX)
                    pixels[i + 3] = (byte)(255 - pixels[i + 3]);
                if (mirrorY)
                    pixels[i + 1] = (byte)(255 - pixels[i + 1]);
            }
        }

        private static byte[] MirrorHorizontal(byte[] pixels, int width, int height)
        {
            var output = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                CopyPixel(pixels, output, width, x, y, width - 1 - x, y);
            return output;
        }

        private static byte[] MirrorVertical(byte[] pixels, int width, int height)
        {
            var output = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                CopyPixel(pixels, output, width, x, y, x, height - 1 - y);
            return output;
        }

        private static byte[] Rotate180(byte[] pixels, int width, int height)
        {
            var output = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                CopyPixel(pixels, output, width, x, y, width - 1 - x, height - 1 - y);
            return output;
        }

        private static byte[] Rotate90Clockwise(byte[] pixels, int width, int height, out int newWidth, out int newHeight)
        {
            newWidth = height;
            newHeight = width;
            var output = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                CopyPixel(pixels, output, width, newWidth, x, y, height - 1 - y, x);
            return output;
        }

        private static byte[] Rotate90CounterClockwise(byte[] pixels, int width, int height, out int newWidth, out int newHeight)
        {
            newWidth = height;
            newHeight = width;
            var output = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                CopyPixel(pixels, output, width, newWidth, x, y, y, width - 1 - x);
            return output;
        }

        private static void CopyPixel(byte[] source, byte[] destination, int sourceWidth, int sourceX, int sourceY, int destinationX, int destinationY)
        {
            var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
            var destinationIndex = ((destinationY * sourceWidth) + destinationX) * 4;
            destination[destinationIndex + 0] = source[sourceIndex + 0];
            destination[destinationIndex + 1] = source[sourceIndex + 1];
            destination[destinationIndex + 2] = source[sourceIndex + 2];
            destination[destinationIndex + 3] = source[sourceIndex + 3];
        }

        private static void CopyPixel(byte[] source, byte[] destination, int sourceWidth, int destinationWidth, int sourceX, int sourceY, int destinationX, int destinationY)
        {
            var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
            var destinationIndex = ((destinationY * destinationWidth) + destinationX) * 4;
            destination[destinationIndex + 0] = source[sourceIndex + 0];
            destination[destinationIndex + 1] = source[sourceIndex + 1];
            destination[destinationIndex + 2] = source[sourceIndex + 2];
            destination[destinationIndex + 3] = source[sourceIndex + 3];
        }
    }
}
