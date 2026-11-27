# SnipNation

## Repository language

- Use English for all repository text, including documentation, code comments, app and installer text, accessibility labels, error messages, logs, scripts, workflow labels, test fixtures, commit messages, and release notes.
- Keep the README limited to a brief product overview and a short usage guide.

## Branding requirements

- Use only the user-supplied **SnipNation-Brand-Kit.zip** for SnipNation branding. This applies to the app, tray icon, installer, documentation, repository images, and any future interfaces.
- Use the original logos, icons, and banners from the kit. Preserve their proportions and the spelling **SnipNation**. Use the original dark Windows icon in **src/SnipNation/Assets/SnipNation.ico** for the app and installer, and the light and dark banners in **assets/branding** for the README.
- Use only the colors defined in **assets/branding/palette.json**. The app uses the dark palette with **#15171C**, **#C6FA78**, **#FFFFFF**, and **#A0A0A0**. Use the kit palette for controls, focus indicators, the capture outline, and the default colors for new annotations. Reference app colors through **src/SnipNation/Branding/Palette.xaml**.
- Use Inter for app typography. The bundled fonts and their license are in **src/SnipNation/Assets/Fonts**.
- Change the logos, typography, or palette only when the user explicitly supplies an updated kit.

## Continuous integration

- Use Ubuntu runners for CI. Build the Windows target with warnings treated as errors and run all solution tests that can run on Ubuntu.
- Keep **Warning-free build and tests** required on **main** and require pull requests to be up to date before merging. Enable GitHub auto-merge for ready pull requests from branches in this repository.
