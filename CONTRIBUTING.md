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

GitHub Actions renders the capture, gallery, and editor views on Windows. Download **snipnation-screenshots** from a pull request's **Build and test** run to review the current images. Successful builds of `main` publish those images to `readme-screenshots`.

When an interface change affects a screenshot, check the result and update the sample content in `tools/SnipNation.Screenshots` if needed. Use examples you can share publicly. Show the toolbar and the resulting annotations.

To produce the same images on Windows after building, run the following command.

```powershell
dotnet run --project tools/SnipNation.Screenshots -c Release --no-build --no-restore -- artifacts/screenshots
```
