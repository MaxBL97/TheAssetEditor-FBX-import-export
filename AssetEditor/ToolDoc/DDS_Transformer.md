# DDS Transformer

Transforms DDS files by converting them to a temporary PNG, applying rotation/mirroring, then encoding them back to DDS.

## Supported transforms

- Rotation: `0`, `90`, `180`, `270` degrees.
- Mirror X.
- Mirror Y.

## Normal map warning

Mirroring a normal map is not only a pixel-position operation. The tangent-space channels must also be flipped. For TW orange normals, the tool can adjust:

```text
Mirror X -> invert alpha channel
Mirror Y -> invert green channel
```

Rotated normal maps should still be reviewed visually because tangent-space rotation can be asset-specific.
