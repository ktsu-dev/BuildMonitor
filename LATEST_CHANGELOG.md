## v1.6.1 (patch)

Changes since v1.6.0:

- refactor: hand the Azure DevOps session back alongside its lease ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: clear only the credential GitHub rejected, and stop treating a plain 403 as one [patch] ([@matt-edmondson](https://github.com/matt-edmondson))
- fix: keep a replaced Azure DevOps session open until its last holder releases it [patch] ([@matt-edmondson](https://github.com/matt-edmondson))

