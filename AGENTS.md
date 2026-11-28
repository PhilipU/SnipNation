# SnipNation

## Repository language

- Use English for all repository text, including documentation, code comments, app and installer text, accessibility labels, error messages, logs, scripts, workflow labels, test fixtures, commit messages, and release notes.
- Keep the README focused on a brief product overview, badges, current app screenshots, and a short usage guide. Put contribution and development instructions in **CONTRIBUTING.md**.
- Keep the screenshot tool aligned with the capture, gallery, and editor views. Include the editing modes and follow the screenshot guidance in **CONTRIBUTING.md**.

## Branding requirements

- Use only the user-supplied **SnipNation-Brand-Kit.zip** for SnipNation branding. This applies to the app, tray icon, installer, documentation, repository images, and any future interfaces.
- Use the original logos, icons, and banners from the kit. Preserve their proportions and the spelling **SnipNation**. Use the original dark Windows icon in **src/SnipNation/Assets/SnipNation.ico** for the app and installer, and the light and dark banners in **assets/branding** for the README.
- Use only the colors defined in **assets/branding/palette.json** for the interface, including controls, focus indicators, and the capture outline. The app uses the dark palette with **#15171C**, **#C6FA78**, **#FFFFFF**, and **#A0A0A0**. Reference interface colors through **src/SnipNation/Branding/Palette.xaml**.
- Annotation colors are independent of the brand palette. This applies to drawn annotations, their preset previews, and color swatches.
- Use Inter for interface typography. Text annotations may use other fonts. The bundled Inter fonts and their license are in **src/SnipNation/Assets/Fonts**.
- Change the logos, interface typography, or interface palette only when the user explicitly supplies an updated kit.

## Continuous integration

- Use standard GitHub-hosted Ubuntu and Windows runners for CI. Restore locked dependencies, build with warnings treated as errors, and run the solution tests on both platforms.
- Render README screenshots on Windows and publish them from successful builds of **main** to the **readme-screenshots** branch.
- Keep **Warning-free build and tests** required on **main** and require pull requests to be up to date before merging. Enable GitHub auto-merge for ready pull requests from branches in this repository.

## Releases

- Use Conventional Commit subjects so Release Please can determine the next version. The changelog and release notes list merged pull requests since the previous release.
- Run **Actions → Release SnipNation → Run workflow** on **main** to request a release. The workflow runs the required check on the release pull request before merging it. It packages the Windows installer and Velopack update files on Ubuntu before publishing.
