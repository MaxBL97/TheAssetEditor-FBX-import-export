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
        private const string DefaultOutputMaterialMapFolder = "ConvMaterialMap";

        public TextureToolRunResult ConvertDdsToPng(TextureToolOptions options)
        {
            var log = new List<string>();
            var files = EnumerateInputFiles(options.InputPath, ".dds", options.Recursive).ToList();
            if (files.Count == 0)
                return WarnOnly(log, "No DDS files found.");

            ValidateTexconv(options.TexconvPath);

            log.Add($"DDS -> PNG settings: kind={options.TextureKind}, output={(options.OutputBesideInput ? "beside input" : (string.IsNullOrWhiteSpace(options.OutputFolderName) ? DefaultOutputPngFolder : options.OutputFolderName.Trim()))}, overwrite={options.Overwrite}, recursive={options.Recursive}");
            log.Add(options.ConvertTwOrangeNormalToBlueNormal
                ? "DDS normal conversion enabled: TW-orange normals are exported as Blender/glTF blue normals."
                : "DDS normal conversion disabled: DDS -> PNG only changes the file format.");
            log.Add(options.ConvertMaterialMapChannels
                ? "Material map channel swap enabled: R/B channels are swapped during the format conversion."
                : "Material map channel swap disabled: material map channels are preserved.");

            var processed = 0;
            var warnings = 0;
            var errors = 0;

            foreach (var file in files)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "AssetEditor_TextureTools", Guid.NewGuid().ToString("N"));
                try
                {
                    var kind = ResolveTextureKind(file, options.TextureKind);
                    if (options.TextureKind == TextureToolKind.Auto && kind == TextureToolKind.BaseColour && !IsRecognizedBaseColourMap(file))
                    {
                        warnings++;
                        log.Add($"WARNING: Auto mode treated '{Path.GetFileName(file)}' as BaseColour because no known suffix was found. Select Texture kind manually if this is wrong.");
                    }

                    var output = ResolveOutputDirectory(file, options.OutputBesideInput, options.OutputFolderName, DefaultOutputPngFolder);
                    Directory.CreateDirectory(output);

                    var needsNormalConversion = options.ConvertTwOrangeNormalToBlueNormal && kind == TextureToolKind.Normal;
                    var needsMaterialMapSwap = options.ConvertMaterialMapChannels && kind == TextureToolKind.MaterialMap;

                    if (!needsNormalConversion && !needsMaterialMapSwap)
                    {
                        RunTexconv(options.TexconvPath, BuildDdsToPngArguments(file, output, options.Overwrite), log);
                        processed++;
                        log.Add($"DDS -> PNG ({kind}, format only): {file}");
                        continue;
                    }

                    Directory.CreateDirectory(tempDir);
                    RunTexconv(options.TexconvPath, BuildDdsToPngArguments(file, tempDir, true), log);
                    var tempPng = Directory.GetFiles(tempDir, "*.png").FirstOrDefault();
                    if (tempPng == null)
                        throw new InvalidOperationException("texconv did not generate a temporary PNG.");

                    var expectedOutput = Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".png");
                    if (File.Exists(expectedOutput))
                    {
                        if (!options.Overwrite)
                            throw new IOException($"Output already exists: {expectedOutput}");
                        File.Delete(expectedOutput);
                    }

                    TransformPng(tempPng, expectedOutput, 0, false, false, false, needsNormalConversion, needsMaterialMapSwap, false);
                    processed++;
                    log.Add($"DDS -> PNG ({kind}, channel conversion): {file}");
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {file}: {ex.Message}");
                }
                finally
                {
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            }

            return new TextureToolRunResult(processed, warnings, errors, log);
        }

        public TextureToolRunResult ConvertPngToDds(TextureToolOptions options)
        {
            var log = new List<string>();
            var files = EnumerateInputFiles(options.InputPath, ".png", options.Recursive).ToList();
            if (files.Count == 0)
                return WarnOnly(log, "No PNG files found.");

            ValidateTexconv(options.TexconvPath);

            log.Add($"PNG -> DDS settings: kind={options.TextureKind}, output={(options.OutputBesideInput ? "beside input" : (string.IsNullOrWhiteSpace(options.OutputFolderName) ? DefaultOutputDdsFolder : options.OutputFolderName.Trim()))}, overwrite={options.Overwrite}, recursive={options.Recursive}");
            log.Add(options.ConvertBlueNormalToTwOrangeNormal
                ? "Normal conversion enabled: blue/purple normals are repacked to TW orange."
                : "Normal conversion disabled: normal channels are preserved.");
            log.Add(options.ConvertMaterialMapChannels
                ? "Material map channel swap enabled: R/B channels are swapped before DDS compression."
                : "Material map channel swap disabled: material map channels are preserved.");

            var processed = 0;
            var warnings = 0;
            var errors = 0;

            foreach (var file in files)
            {
                try
                {
                    var kind = ResolveTextureKind(file, options.TextureKind);
                    if (options.TextureKind == TextureToolKind.Auto && kind == TextureToolKind.BaseColour && !IsRecognizedBaseColourMap(file))
                    {
                        warnings++;
                        log.Add($"WARNING: Auto mode treated '{Path.GetFileName(file)}' as BaseColour because no known suffix was found. Select Texture kind manually if this is wrong.");
                    }
                    if (kind == TextureToolKind.MaterialMap)
                        log.Add(options.ConvertMaterialMapChannels
                            ? $"INFO: MaterialMap '{Path.GetFileName(file)}' will have R/B swapped, then be compressed as BC1_UNORM linear. No specular/gloss combine is performed here."
                            : $"INFO: MaterialMap '{Path.GetFileName(file)}' is compressed as BC1_UNORM linear with channels preserved. No specular/gloss combine is performed here.");
                    if (kind == TextureToolKind.Normal && !options.ConvertBlueNormalToTwOrangeNormal)
                        log.Add($"INFO: Normal conversion is OFF for '{Path.GetFileName(file)}'. Use this only for already TW-orange packed normals.");

                    var output = ResolveOutputDirectory(file, options.OutputBesideInput, options.OutputFolderName, DefaultOutputDdsFolder);
                    Directory.CreateDirectory(output);

                    var inputForTexconv = file;
                    string? temporaryPng = null;

                    var needsNormalConversion = options.ConvertBlueNormalToTwOrangeNormal && kind == TextureToolKind.Normal;
                    var needsMaterialMapSwap = options.ConvertMaterialMapChannels && kind == TextureToolKind.MaterialMap;
                    var needsGeometryTransform = options.RotationDegrees != 0 || options.MirrorX || options.MirrorY;

                    if (needsNormalConversion || needsMaterialMapSwap || needsGeometryTransform)
                    {
                        temporaryPng = Path.Combine(output, $"__ae_texture_tool_{Guid.NewGuid():N}.png");
                        TransformPng(file, temporaryPng, options.RotationDegrees, options.MirrorX, options.MirrorY, needsNormalConversion, false, needsMaterialMapSwap, options.AdjustTwNormalChannelsForMirror && kind == TextureToolKind.Normal);
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
                    TransformPng(tempPng, transformed, options.RotationDegrees, options.MirrorX, options.MirrorY, false, false, options.ConvertMaterialMapChannels && kind == TextureToolKind.MaterialMap, options.AdjustTwNormalChannelsForMirror && kind == TextureToolKind.Normal);

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


        public TextureToolRunResult BuildMaterialMap(MaterialMapBuildOptions options)
        {
            var log = new List<string>();
            if (string.IsNullOrWhiteSpace(options.SpecularInputPath) && string.IsNullOrWhiteSpace(options.GlossInputPath))
                return WarnOnly(log, "Set at least one input: specular, gloss, or both.");

            ValidateTexconv(options.TexconvPath);

            var processed = 0;
            var warnings = 0;
            var errors = 0;
            var pairs = BuildMaterialMapPairs(options, log, ref warnings);

            if (pairs.Count == 0)
                return WarnOnly(log, "No PNG or DDS specular/gloss files found.");

            log.Add($"Material map builder: pairs={pairs.Count}, output={(options.OutputBesideInput ? "beside input" : (string.IsNullOrWhiteSpace(options.OutputFolderName) ? DefaultOutputMaterialMapFolder : options.OutputFolderName.Trim()))}, overwrite={options.Overwrite}, recursive={options.Recursive}");
            log.Add("Output rule: R = specular intensity/metalness, G = roughness from gloss, B = 0, A = 255, DDS = BC1_UNORM linear.");
            log.Add(options.InvertGlossToRoughness
                ? "Gloss rule: roughness = 255 - gloss. This is the usual gloss/smoothness -> roughness conversion."
                : "Gloss rule: roughness = gloss. Use this only if your gloss input is already authored as roughness.");
            log.Add($"Missing specular fallback: metalness={ClampByte(options.DefaultMetalness)}. Missing gloss fallback: roughness={ClampByte(options.DefaultRoughness)}.");

            foreach (var pair in pairs)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "AssetEditor_TextureTools", Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(tempDir);

                    byte[]? specularPixels = null;
                    byte[]? glossPixels = null;
                    var width = 0;
                    var height = 0;

                    if (pair.SpecularPath != null)
                    {
                        specularPixels = LoadTextureAsBgra(pair.SpecularPath, options.TexconvPath, log, tempDir, out width, out height);
                    }

                    if (pair.GlossPath != null)
                    {
                        glossPixels = LoadTextureAsBgra(pair.GlossPath, options.TexconvPath, log, tempDir, out var glossWidth, out var glossHeight);
                        if (specularPixels == null)
                        {
                            width = glossWidth;
                            height = glossHeight;
                        }
                        else if (glossWidth != width || glossHeight != height)
                        {
                            throw new InvalidOperationException($"Specular/gloss size mismatch for '{pair.Key}': specular={width}x{height}, gloss={glossWidth}x{glossHeight}.");
                        }
                    }

                    if (width <= 0 || height <= 0)
                        throw new InvalidOperationException($"Unable to resolve input size for '{pair.Key}'.");

                    if (pair.SpecularPath == null)
                    {
                        warnings++;
                        log.Add($"WARNING: '{pair.Key}' has no specular input. Using default metalness {ClampByte(options.DefaultMetalness)}.");
                    }

                    if (pair.GlossPath == null)
                    {
                        warnings++;
                        log.Add($"WARNING: '{pair.Key}' has no gloss input. Using default roughness {ClampByte(options.DefaultRoughness)}.");
                    }

                    var materialPixels = BuildMaterialMapPixels(specularPixels, glossPixels, options.InvertGlossToRoughness, ClampByte(options.DefaultMetalness), ClampByte(options.DefaultRoughness), width, height);
                    var materialPng = Path.Combine(tempDir, pair.Key + "_material_map.png");
                    SavePng(materialPng, materialPixels, width, height);

                    var anchor = pair.SpecularPath ?? pair.GlossPath ?? options.SpecularInputPath ?? options.GlossInputPath;
                    var output = ResolveOutputDirectory(anchor, options.OutputBesideInput, options.OutputFolderName, DefaultOutputMaterialMapFolder);
                    Directory.CreateDirectory(output);

                    var expectedDds = Path.Combine(output, pair.Key + "_material_map.dds");
                    if (File.Exists(expectedDds) && !options.Overwrite)
                        throw new IOException($"Output already exists: {expectedDds}");

                    RunTexconv(options.TexconvPath, BuildPngToDdsArguments(materialPng, output, TextureToolKind.MaterialMap, options.Overwrite), log);
                    MoveTexconvOutputToExpectedName(materialPng, materialPng, output, ".dds", options.Overwrite);
                    processed++;
                    log.Add($"Material map built: {expectedDds}");
                }
                catch (Exception ex)
                {
                    errors++;
                    log.Add($"ERROR: {pair.Key}: {ex.Message}");
                }
                finally
                {
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
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


        private sealed record MaterialMapSourcePair(string Key, string? SpecularPath, string? GlossPath);

        private static List<MaterialMapSourcePair> BuildMaterialMapPairs(MaterialMapBuildOptions options, List<string> log, ref int warnings)
        {
            var specularFiles = EnumerateTextureFiles(options.SpecularInputPath, options.Recursive).ToList();
            var glossFiles = EnumerateTextureFiles(options.GlossInputPath, options.Recursive).ToList();

            if (!string.IsNullOrWhiteSpace(options.SpecularInputPath) && specularFiles.Count == 0)
            {
                warnings++;
                log.Add("WARNING: Specular input path was set, but no PNG/DDS files were found.");
            }

            if (!string.IsNullOrWhiteSpace(options.GlossInputPath) && glossFiles.Count == 0)
            {
                warnings++;
                log.Add("WARNING: Gloss input path was set, but no PNG/DDS files were found.");
            }

            var specularByKey = specularFiles
                .GroupBy(GetMaterialBaseKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var glossByKey = glossFiles
                .GroupBy(GetMaterialBaseKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var keys = specularByKey.Keys.Concat(glossByKey.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var output = new List<MaterialMapSourcePair>();
            foreach (var key in keys)
            {
                specularByKey.TryGetValue(key, out var specular);
                glossByKey.TryGetValue(key, out var gloss);
                output.Add(new MaterialMapSourcePair(SanitizeFileName(key), specular, gloss));
            }

            return output;
        }

        private static IEnumerable<string> EnumerateTextureFiles(string inputPath, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                yield break;

            if (File.Exists(inputPath))
            {
                if (IsSupportedMaterialInput(inputPath))
                    yield return inputPath;
                yield break;
            }

            if (!Directory.Exists(inputPath))
                yield break;

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(inputPath, "*", option))
            {
                if (IsSupportedMaterialInput(file))
                    yield return file;
            }
        }

        private static bool IsSupportedMaterialInput(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetMaterialBaseKey(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var lower = name.ToLowerInvariant();
            var suffixes = new[]
            {
                "_specular_map", "_specular", "_spec", "_spc", "_s",
                "_gloss_map", "_gloss", "_glossiness", "_smoothness", "_roughness", "_g",
                "_material_map", "_mat_map", "_mat"
            };

            foreach (var suffix in suffixes)
            {
                if (lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name[..^suffix.Length];
            }

            return name;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "material" : value.Trim();
        }

        private static byte[] LoadTextureAsBgra(string file, string texconvPath, List<string> log, string tempDir, out int width, out int height)
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                return CopyPixels(LoadBitmap(file), out width, out height);
            }

            if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                var ddsTempDir = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(ddsTempDir);
                RunTexconv(texconvPath, BuildDdsToPngArguments(file, ddsTempDir, true), log);
                var png = Directory.GetFiles(ddsTempDir, "*.png").FirstOrDefault()
                    ?? throw new InvalidOperationException($"texconv did not decode DDS input '{file}'.");
                return CopyPixels(LoadBitmap(png), out width, out height);
            }

            throw new NotSupportedException($"Unsupported material input format: {extension}");
        }

        private static byte[] BuildMaterialMapPixels(byte[]? specularPixels, byte[]? glossPixels, bool invertGlossToRoughness, byte defaultMetalness, byte defaultRoughness, int width, int height)
        {
            var output = new byte[width * height * 4];
            for (var i = 0; i < output.Length; i += 4)
            {
                var metalness = defaultMetalness;
                if (specularPixels != null)
                    metalness = ToLuma(specularPixels[i + 2], specularPixels[i + 1], specularPixels[i + 0]);

                var roughness = defaultRoughness;
                if (glossPixels != null)
                {
                    var gloss = ToLuma(glossPixels[i + 2], glossPixels[i + 1], glossPixels[i + 0]);
                    roughness = invertGlossToRoughness ? (byte)(255 - gloss) : gloss;
                }

                output[i + 0] = 0;         // B unused for CA material maps produced by this helper.
                output[i + 1] = roughness; // G roughness.
                output[i + 2] = metalness; // R specular/metalness intensity.
                output[i + 3] = 255;       // A unused/opaque.
            }

            return output;
        }

        private static byte ToLuma(byte r, byte g, byte b)
        {
            var value = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        }

        private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

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
            if (IsRecognizedBaseColourMap(path))
                return TextureToolKind.BaseColour;

            return TextureToolKind.BaseColour;
        }

        private static bool IsRecognizedBaseColourMap(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return name.EndsWith("_base_colour")
                || name.EndsWith("_basecolor")
                || name.EndsWith("_bc")
                || name.EndsWith("_diffuse")
                || name.EndsWith("_d");
        }

        private static bool IsNormalMap(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return name.EndsWith("_n") || name.EndsWith("_normal") || name.EndsWith("_normal_map");
        }

        private static IReadOnlyList<string> BuildDdsToPngArguments(string inputFile, string outputFolder, bool overwrite)
        {
            var args = new List<string>();
            if (overwrite)
                args.Add("-y");

            args.AddRange(["-ft", "png", "-o", outputFolder, inputFile]);
            return args;
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

        private static void TransformPng(string input, string output, int rotationDegrees, bool mirrorX, bool mirrorY, bool blueNormalToTwOrange, bool twOrangeNormalToBlue, bool swapMaterialMapChannels, bool adjustTwNormalForMirror)
        {
            var bitmap = LoadBitmap(input);
            var pixels = CopyPixels(bitmap, out var width, out var height);

            if (blueNormalToTwOrange)
                ConvertBlueNormalToTwOrange(pixels);

            if (twOrangeNormalToBlue)
                ConvertTwOrangeNormalToBlue(pixels);

            if (swapMaterialMapChannels)
                SwapRedBlueChannels(pixels, forceOpaqueAlpha: true);

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

        private static void ConvertTwOrangeNormalToBlue(byte[] pixels)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var oldA = pixels[i + 3];
                var oldG = pixels[i + 1];
                pixels[i + 0] = 255;                              // B
                pixels[i + 1] = GammaComponent(oldG, 1.0f / 2.2f); // G
                pixels[i + 2] = oldA;                             // R stores previous alpha/X
                pixels[i + 3] = 255;                              // A
            }
        }

        private static void SwapRedBlueChannels(byte[] pixels, bool forceOpaqueAlpha)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i + 0], pixels[i + 2]) = (pixels[i + 2], pixels[i + 0]);
                if (forceOpaqueAlpha)
                    pixels[i + 3] = 255;
            }
        }

        private static byte GammaComponent(byte c, float gamma)
        {
            var v = c / 255.0;
            var corrected = Math.Pow(v, gamma) * 255.0;
            return (byte)Math.Clamp((int)Math.Round(corrected, MidpointRounding.AwayFromZero), 0, 255);
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
