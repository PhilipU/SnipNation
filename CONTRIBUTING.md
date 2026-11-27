# Contributing

Use [GitHub Issues](https://github.com/PhilipU/SnipNation/issues) to report bugs and suggest features. For a security vulnerability, follow the [security policy](SECURITY.md). Follow the [code of conduct](CODE_OF_CONDUCT.md) when taking part.

Open pull requests against `main`. Write in English and use Conventional Commit subjects such as `fix: preserve annotation color` so Release Please can determine the next version.

Use the .NET SDK selected in [global.json](global.json). Run the build and tests before opening your pull request.

```sh
dotnet restore SnipNation.slnx --locked-mode -warnaserror
dotnet build SnipNation.slnx -c Release --no-restore -warnaserror
dotnet test SnipNation.slnx -c Release --no-build --no-restore
```

## README screenshots

Retake the affected images in `assets/screenshots/` when you change the capture, gallery, or editor interface. Use the current Windows build and example content you can share publicly. Show the toolbar and the resulting annotations in editor screenshots. Replace the existing files and update their README captions when the examples change.
