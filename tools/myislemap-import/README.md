# My Isle Map offline snapshot importer

Run manually from the repository root:

```powershell
node tools/myislemap-import/import.mjs
```

The importer only accepts the pinned `https://myislemap.com` sources declared in
`import.mjs`. It writes the audited raw JavaScript, sanitized SVG sources, 64px PNG
runtime icons, and the embedded schema-v2 catalog. It fails if the expected source
inventory changes, so a website update must be reviewed rather than silently entering
a release.

The application never executes this importer and never requests My Isle Map at runtime.
