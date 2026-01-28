## v1.2.7 (patch)

Changes since v1.2.6:

- Implement dynamic column width management in build monitor table ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor column width handling to use a dictionary and improve width retrieval logic in build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix color indicator logic for build status in the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor SaveColumnWidths method to use ImGuiTablePtr for column width retrieval ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix progress bar width in build monitor to allow for dynamic sizing ([@matt-edmondson](https://github.com/matt-edmondson))
- Validate column widths before saving to prevent corrupted values in build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Set search box widths to dynamic sizing in build monitor table ([@matt-edmondson](https://github.com/matt-edmondson))
