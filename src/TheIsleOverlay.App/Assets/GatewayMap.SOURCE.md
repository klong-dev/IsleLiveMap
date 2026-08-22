# Gateway map texture

- Upstream: `https://myislemap.com/assets/gateway-map.webp?v=20260809v1`
- Retrieved: 2026-08-21
- SHA-256: `BA2E5E614995BEC84559B950F1AE978C2F9A66743F0DA47A348278DB01557EF3`
- Dimensions: `7800 x 7817`

The original WebP is retained as the audited source. The application embeds a JPEG
derivative (`GatewayMap.jpg`, ffmpeg quality 2) because WPF can decode JPEG natively
on every supported Windows installation, while WebP requires an optional WIC codec.

- Embedded JPEG SHA-256: `D773E50DDD5FD691D4F751454F972EB49E70243E3326789BE9D6E32913481BB7`
- Embedded JPEG dimensions: `7800 x 7817`
- Embedded JPEG size: `13,787,683 bytes`

The map remains fully local; startup never depends on downloading the texture.
