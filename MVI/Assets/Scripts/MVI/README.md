# Loxodon Framework MVI (UPM)

## Install via Git URL

Add this dependency in Unity `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.mikinuo.loxodon-framework-mvi": "https://github.com/<your-org>/<your-repo>.git?path=/MVI/Assets/Scripts/MVI"
  }
}
```

## What is included

- Core MVI runtime: `Core/`
- Loxodon integration and UGUI adapters: `Loxodon/`
- FairyGUI integration and adapters: `FairyGUI/`
- Editor tooling (DevTools window): `Editor/`

## Notes

- This package assumes your project already includes required third-party dependencies, for example:
  - `R3.Unity`
  - `Loxodon.Framework`
  - `FairyGUI` (only required when using FairyGUI integration)
- For setup and integration examples, see repository root `README.md`.
