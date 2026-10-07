## v1.6.6 (patch)

Changes since v1.6.5:

- Test the status bar directly instead of extracting a per-provider method ([@Claude](https://github.com/Claude))
- Test the column filters through the table's own row checks ([@Claude](https://github.com/Claude))
- Cover both tooltip call sites with hover tests ([@Claude](https://github.com/Claude))
- fix: make Regex, Fuzzy and prefixed Glob column filters work [patch] ([@Claude](https://github.com/Claude))
- fix: show build-log and provider text in tooltips without printf formatting [patch] ([@Claude](https://github.com/Claude))
- fix: report a rejected Azure DevOps PAT as AuthFailed instead of faulting the update loop [patch] ([@Claude](https://github.com/Claude))

