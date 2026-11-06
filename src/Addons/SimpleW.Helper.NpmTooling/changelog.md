# Changelog

## Unreleased

### feature

- Configure frontend paths and the API URL once in the `.csproj`, shared by local development and publication through application metadata.
- Add `SimpleWNpmPublishPath` for the frontend destination and validate shared settings before publishing with `--no-build`.
- Select the frontend mode with `NpmEnvironment`: `AutoDetect` (default) uses the application's publication marker independently of Debug/Release; `Development` and `Production` force a mode.
- Validate the application's publication marker before running npm, including when using `dotnet publish --no-build`.
- Add npm development server supervision with separate server/client logging and cleanup of owned processes.
- Supply `VITE_SIMPLEW_API_URL` and optional development CORS rules for direct browser calls to SimpleW.
- Serve compiled SPA files in production with an HTML navigation fallback that excludes API paths and missing assets.
- Add publish targets for frontend dependency installation, compilation and copying assets to the configured destination (`wwwroot` by default).
