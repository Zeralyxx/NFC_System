# NFC System Branding Concepts

Created September 21, 2026 with the built-in image generation tool, not the API/CLI fallback.

## Deliverables

- `nfc-system-icon-v1.png`: 1254 x 1254 transparent RGBA PNG. Icon source artwork.
- `nfc-system-logo-v1.png`: 2172 x 724 transparent RGBA PNG. Horizontal wordmark for light backgrounds.

Both files preserve the generator's original output. Alpha transparency was checked. Integration uses new files under `NFC_System/Assets/Branding`; the previous assets remain intact.

## App Integration

- `Build-WindowsBrandAssets.ps1` resizes and encodes the approved artwork into Windows PNG scale/target-size variants and a multiresolution `AppIcon.ico`. Run it only when the source artwork changes, not during normal installation or QR provisioning.
- The project embeds the ICO in the executable and includes the new assets in the package. The package manifest uses them for application/store icons, tiles and the splash screen.
- Light-background tiles and the splash use the original full logo. Login, initialization and dashboard screens use `BrandLogo`, which combines the approved emblem with native white WinUI text for sharp dark-background rendering. The kiosk header uses the emblem.
- The WinUI smoke test checks image loading, 320/640-pixel layouts and ICO assignment, and produces `branding-320.png` and `branding-640.png` beside its executable.
- A generated white-letter raster variant was rejected because of rough text edges; it is not included in the app.

Building does not necessarily update an already installed development package. Close the running app, then use Visual Studio's Deploy command for Debug x64 before testing the installed copy. No package identity, QR keys or database settings are changed by branding.

Changing branding does not rotate QR signing keys or require rerunning QR provisioning.

## Icon Prompt

Use case: logo-brand. Create one finished Windows desktop app icon for an existing student attendance and access-control application named NFC System. Icon only, NO TEXT. A distinctive simple identity-card silhouette in deep emerald green, with a bold clean white checkmark inside and two short cyan/teal contactless signal arcs at its upper right. Flat, crisp, vector-like raster artwork, thick balanced shapes, minimal detail, professional campus/security software rather than a finance or antivirus brand. Strong silhouette and readability at 24 and 32 pixels. Center the single mark in a square 1024x1024 canvas with generous uniform 14% safe margins. Genuinely transparent background with alpha. No mockup, no app-store sheet, no surrounding rounded-square tile, no gradient, no shadow, no glow, no watermark, no text, no additional variants. Keep all elements comfortably inside the canvas. Deliver a high-resolution transparent PNG.

## Logo Prompt

Reference image: the generated icon above.

Use case: logo-brand. Asset type: matching horizontal product logo for NFC System, a student attendance and access-control desktop application. Input image 1 is the established brand-mark reference. Preserve its design faithfully: emerald ID badge with white person and white checkmark, cyan contactless arcs. Create one clean horizontal logo lockup on a genuinely transparent background with alpha. Place a compact version of that same icon on the left, followed on the right by the exact text 'NFC System' in an exceptionally crisp, modern bold geometric sans-serif, neutral near-black charcoal lettering. Text must be spelled exactly NFC System, one line, balanced size relative to the mark. Clean spacing, flat raster artwork with vector-like edges, professional institutional software identity. Wide landscape composition, approximately 3:1 aspect ratio, generous clear padding, no mockup or presentation board, no extra text or tagline, no frame, no drop shadow, no gradients, no watermark. Produce a high-resolution PNG logo suitable for light backgrounds. One final asset, not variants.
