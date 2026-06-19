# DDS to PNG

Converts DDS files to PNG files through `texconv.exe`.

## Inputs

- A single `.dds` file, or
- A folder containing `.dds` files.

## Options

- **Recursive folder scan**: processes subfolders.
- **Output beside input**: writes the PNG next to the DDS.
- **Output folder name**: when not writing beside input, creates a folder such as `ConvPNG` next to each DDS.

## Output

```text
source_folder/texture.dds
source_folder/ConvPNG/texture.png
```
