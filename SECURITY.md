# Transflux security policy

Security is a release gate for every milestone, not a final-stage cleanup.

- Treat every imported file as untrusted. Do not parse untrusted files inside the desktop process. Use isolated, non-elevated workers with cancellation, deadlines, bounded concurrency and resource limits. Process isolation alone is not a sandbox.
- Keep medical data local. No telemetry containing document contents, patient identifiers, or paths; no cloud extraction without explicit approval. Never commit patient data or real migration fixtures.
- Preserve sources by default; validate and stage outputs before publishing. Consider CSV formula injection and use text-only spreadsheet import for raw migration CSVs.
- Review direct and transitive dependencies, upstream advisories, provenance, licensing and maintenance before adoption. NuGet audit is enabled repository-wide, including transitive packages; high/critical findings and unavailable audit data fail restore. Dependabot checks NuGet and GitHub Actions weekly.
- Require reviewed dependency lock files, immutable action pins, least-privilege workflow tokens, signed releases, and security regression tests before production release. Do not suppress an audit failure without documented review.
- Prefer hard OS resource quotas and restricted filesystem/network permissions over monitoring alone. These are required hardening work, not claims about current isolation.
- PdfPig CVE-2026-135173: 0.1.16 replaced with exact 0.1.17-alpha-202611230028-4a6b5. Upstream compare confirms source commit 4a6b5 descends from fix d1d74b19f2b4714ead5eef364fea51ee51a21e0b (21 ahead, 0 behind). Reassess when patched stable version ships. Verify package provenance and runtime behavior before release.
- Report suspected vulnerabilities privately to the ZH Systems repository maintainer; do not attach patient data or publish exploit details.

Known gaps: lock files and OS-level sandbox/quotas are not yet implemented; audit checks do not establish absence of unknown vulnerabilities. Existing image and Ghostscript paths need equivalent review. Windows build and isolated-worker integration tests are required before release.
