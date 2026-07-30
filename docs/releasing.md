# Releasing packages with NuGet Trusted Publishing

Lifecycle.NET publishes from GitHub Actions using NuGet Trusted Publishing. It does not store a long-lived NuGet API key in GitHub.

## One-time setup

1. On nuget.org, open **Trusted Publishing** and add a GitHub Actions policy for this repository.
2. Set the workflow file to `publish-nuget.yml` (the file name only).
3. Set the policy environment to `nuget.org`.
4. In GitHub repository variables, set `NUGET_USERNAME` to the owning nuget.org profile name. This is an identifier, not a secret.
5. Configure the GitHub `nuget.org` environment with required reviewers if releases need approval.

## Release flow

Push a version tag such as `v0.1.0-alpha.2`. The workflow restores, builds, tests, packs with the tag version, requests a short-lived OIDC credential from NuGet, and publishes the `.nupkg` files. Symbol packages are produced but not pushed by this workflow.

The workflow has no fallback API key. If Trusted Publishing is unavailable for the account, do not add a broad publish secret; publish manually until the account is eligible.
