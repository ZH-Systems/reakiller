# Transflux

Transflux (formerly ReaKiller) is a local Windows file conversion and compression utility by ZH Systems. The initial rebrand retains legacy executable names, installation directory, registry key, report names and release asset names for compatibility with existing automation and installed updaters. See [ROADMAP.md](ROADMAP.md) for the Transflux 2.0 development plan.

## Important sample finding
All 18 IMG files provided in test.7z are **standard JPEG files with an IMG extension**. The program recognizes JPEG headers inside IMG files. Other IMG encodings are not guaranteed to work.

## Build (Windows with .NET 10 SDK)

```powershell
dotnet restore .\ReaKiller\ReaKiller.csproj
dotnet publish .\ReaKiller\ReaKiller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

## Interactive console

Run `ReaKiller.exe` without arguments to enter the guided console. It asks for the source folder, whether to replace originals in place, the output folder when applicable, recursion, worker count, maximum width, and overwrite policy. A text progress bar displays conversion progress.

**In-place replacement**: `--in-place` saves verified PNG output beside each source and deletes the original only when all pages converted successfully. Existing output PNGs are not overwritten unless `--overwrite` is specified. Make a backup first: deleting originals can remove source metadata and embedded DICOM information.

## Usage

```cmd
ReaKiller.exe convert "D:\Migrations\Thornton\romexis_images" --output "D:\Migrations\Thornton\Converted" --recursive --threads 4
ReaKiller.exe convert "D:\Images\I11624.img" --output "D:\Converted"
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --max-width 2048
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --dry-run
```

Supports extensions: .img, .rvg, .dcm, .dic, .dicom, .jpg, .jpeg, .tif, .tiff (plus PNG, BMP, CMP, and PCX). Options include --recursive, --threads N, --overwrite, --dry-run, --max-width, --max-height. Output names include the source extension to avoid collisions (e.g. I11624_img.png). Multipage output has page-number suffixes. Results are logged to reakiller-report.csv. Originals are preserved by default. With `--in-place`, the source file is deleted after validated PNG conversion. Output must be outside the input directory tree.

## Carestream RVG display contrast

For Carestream RVG files without a DICOM Window Center/Width, the converter now derives a display-only intensity window from the nonzero 5th and 95th percentile of each decoded frame. Zero-valued padding is excluded from the window estimate, and output is clipped to that window before scaling to 16-bit PNG/JPEG display intensities. Sources are never modified. This is a contrast-enhanced presentation export, not a bit-for-bit equivalent of the raw radiograph. Other image formats are unchanged. Image fidelity, orientation, and clinically relevant grayscale appearance must be evaluated against a trusted Carestream export. For images with a supplied Window Center/Width, the current path retains its existing full-range behavior rather than applying that DICOM display window.

## Carestream RVG images

Carestream `.rvg` files are accepted by the CLI and Windows image picker. An actual supplied RVG sample was identified as DICOM JPEG Lossless Process 14 SV1 (transfer syntax `1.2.840.10008.1.2.4.70`), which ImageMagick cannot decode directly. Encapsulated RVG DICOM files are now transcoded to temporary uncompressed Explicit VR Little Endian DICOM files with `fo-dicom.Codecs`, rendered through the existing pipeline, and removed in a `finally` block. Standard JPEG, TIFF, PNG and BMP payloads also remain supported. DICOM RVG files are checked for PixelData before conversion. Unsupported or proprietary RVG encodings fail explicitly and leave source files intact. This supports known DICOM-encoded Carestream RVG formats, including JPEG Lossless Process 14 SV1, but **not** undocumented proprietary formats. Validate output contrast, orientation, dimensions, and diagnostic appearance against the source Carestream application. Windows native codec deployment and end-to-end conversion require validation. Original file timestamps follow the existing preservation behavior.

## Limitations

PNG is lossless. Converting a compressed JPEG to PNG commonly makes it larger and cannot restore discarded JPEG detail. Resizing is optional and reduces resolution. The --compression lossless profile uses PNG zlib level 6 and adaptive filtering; --compression max uses level 9 with adaptive filtering. Both preserve pixel data without quality loss.

DICOM files are first parsed by fo-dicom to confirm valid datasets with PixelData. Image rendering still uses ImageMagick's supported transfer syntaxes. DICOM VOI/windowing, inversion, multi-frame behavior, and pixel-depth preservation must be compared with known-good references before clinical use. Unrecognized IMG formats are rejected. No live .NET build or conversion tests were possible in this execution environment. No patient images are committed.

## Automated tests and CI

`dotnet test ReaKiller.Tests/ReaKiller.Tests.csproj -c Release` runs regression cases for JPEG-signature IMG conversion, default input preservation, in-place removal on success, collision skipping, and corrupted inputs. `.github/workflows/build.yml` runs restore/build/tests and publishes a Windows x64 self-contained executable as a workflow artifact if successful. This repository has not yet been verified with a successful Windows build in this session.

## Timestamp preservation

Every generated PNG inherits its source file's filesystem creation time, last-modified time, and last-access time (UTC-based operations), where supported by the filesystem. Timestamps are captured before image decoding and assigned before in-place originals are removed. On filesystems with coarse or unsupported timestamp metadata, values may differ due to OS/filesystem behavior. These are filesystem timestamps, not embedded DICOM acquisition metadata. Existing converted files need re-conversion with `--overwrite` to update their timestamps. Regression tests cover both output-folder and in-place conversion.

## Windows desktop interface

A new WPF interface is in `ReaKiller.Desktop`, styled in an Akitu-inspired blue palette. It includes source-folder browsing, destination selection, optional in-place replacement, recursive scanning, overwrite control, compression profile, thread count, optional maximum width, progress bar, logs, and cancellation. The interface launches the existing CLI converter so unattended CMD workflows remain available.

Build both projects via the Windows GitHub Actions workflow and download the `ReaKiller-Desktop-win-x64` artifact. Extract the **whole bundle** and launch `ReaKiller.Desktop.exe`. Keep `ReaKiller.exe` in the same folder. You can continue to run `ReaKiller.exe` from CMD or PowerShell separately.

The desktop design uses a suggested blue palette rather than a verified official Akitu brand specification. The Windows build and runtime behavior have not yet been verified in this session.

### Desktop UI refresh

The desktop layout now has an Akitu-inspired navy-to-blue header, three clear sections (paths, settings, activity), a persistent action bar, image counting via **Scan images**, a live progress indicator, output logs, an **Open report** action and cancellation. Controls are locked while a conversion runs to prevent changing active settings. In-place replacement retains the existing explicit confirmation. The converter CLI remains unchanged and still handles all image processing and timestamp preservation. Windows compilation and end-to-end testing are required before relying on this updated desktop release.

## Optional image enhancement (desktop and CLI)

The desktop interface now has optional **Auto contrast**, **Contrast** (-50 to +50), and **Sharpening** (0.0 to 2.0) controls. All are off by default. The CLI allows a wider contrast range (-100 to +100).

```cmd
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --contrast 15 --sharpen 0.6
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --auto-contrast
```

Contrast uses Magick.NET brightness/contrast with unchanged brightness. Sharpening uses unsharp masking. Auto contrast uses automatic level stretching. Enhancements affect the PNG pixels and can exaggerate artifacts in medical radiographs. Do not enable these by default for clinical migrations; retain source images and inspect representative outputs. The current implementation has no before/after preview or reusable enhancement presets. These remain potential usability improvements.

The enhancement implementation and associated unit test are committed but are not confirmed by a successful Windows CI build in this session.

## Live desktop progress and file results

The desktop now displays a native WPF progress bar and a live, scrolling results grid. The conversion engine emits one structured `REAKILLER_EVENT:` JSON line per completed source file. Each result is shown as green **Succeeded**, red **Failed**, or gray **Skipped**, with failure details where available. The grid keeps the latest 2,000 results for responsiveness; the full CSV report is retained. The raw console stream is available under **Technical output** for debugging. This also works when multiple worker threads finish in a different order.

The Windows build and new preview UI still require an end-to-end smoke test; the prior working CLI remains accessible.

## Output filename patterns

The desktop UI has an **Output file naming** field with a live example, and patterns are included in saved JSON presets. The CLI accepts `--name-pattern`.

| Token | Meaning |
| --- | --- |
| `{name}` | Original filename without extension |
| `{ext}` | Original extension (without dot) |
| `{index}` | Sequential five-digit number, ordered by source path |
| `{date}` | Source file last-modified date formatted yyyyMMdd |

Default: `{name}_{ext}`, preserving existing behavior. The output extension `.png` is appended automatically. Multipage inputs still receive page suffixes.

Examples:

```cmd
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --name-pattern "Dental_{index}_{name}"
ReaKiller.exe convert "D:\Images" --in-place --recursive --name-pattern "{date}_{name}"
```

The converter checks the resolved names for duplicates before processing, and refuses conflicts rather than silently overwriting converted files. Existing files continue to respect `--overwrite`. As before, backups are recommended with in-place mode. Build and integration validation remain pending.

## Stop conversion (desktop)

The desktop footer provides a red **Stop conversion** button while a batch is running. It writes a per-job temporary stop marker and asks the converter to finish or abandon in-flight image processing without starting additional output commits. Completed PNGs remain; source files that have not completed in-place conversion remain untouched. A stopped file is reported as STOPPED with exit code 3 when applicable. The same CLI also accepts `--stop-file <path>`. This is cooperative cancellation rather than immediate process termination, so an active decode may continue for some time before the batch stops. Windows build/runtime verification is still required.

## Windows publisher metadata and executable signing

Both binaries specify `Company=ZH-Systems`, but Windows treats that as descriptive file metadata, not a trusted signature.

**Recommended: Azure Artifact Signing.** Public-trust signing is available to eligible Canadian organizations. Apply for an Azure Artifact Signing account, pass identity validation, then create a public-trust certificate profile. Configure a Microsoft Entra app with a federated GitHub Actions identity and grant it the **Artifact Signing Certificate Profile Signer** role for the signing profile. The build pipeline supports Azure login via GitHub OIDC and the official `azure/artifact-signing-action@v2`, signing and verifying both bundled EXEs. The following repository secrets are required: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. Repository variables: `ZH_ARTIFACT_SIGNING_ENDPOINT` (for example the endpoint Azure displays for your region), `ZH_ARTIFACT_SIGNING_ACCOUNT`, `ZH_ARTIFACT_SIGNING_PROFILE`, and `ZH_ARTIFACT_SIGNING_ENABLED=true` when fully configured. Leave enabled unset to build **unsigned** without Azure credentials.

Do **not** use the older PFX-in-GitHub-secrets approach for newly issued publicly trusted code-signing certificates: current CA/browser requirements call for protected hardware-backed private keys, which generally cannot be exported to a PFX file. Alternatively obtain an OV code-signing certificate from a CA such as DigiCert and use its supported hardware or cloud signing integration. Neither approach guarantees immediate Windows SmartScreen reputation.

## Source selection: folder or individual files

Use the **Select individual files instead of a folder** toggle in the desktop UI. In individual-file mode, **Browse...** opens a multi-select file picker for IMG, DCM, DICOM, JPG, JPEG, TIF, and TIFF files. Dragging multiple images onto the window also works in this mode. Only those selected files are converted, and the preview uses the first selected image. Turning the toggle off restores whole-folder processing and the recursive scan control.

For automation, the CLI accepts a UTF-8 text file containing one absolute image path per line:

```cmd
ReaKiller.exe convert "D:\Images\first.img" --files-from "D:\Images\selection.txt" --output "D:\Converted" --name-pattern "{index}_{name}"
```

With selected files, non-in-place outputs go directly into the chosen output directory rather than inheriting each file's original directory. Duplicate resolved names are rejected before processing. In-place conversion creates PNGs beside their respective originals and preserves the current source-preservation rules.

## Free self-signed ZH-Systems Windows executables

The repository includes `scripts/Sign-ReaKiller.ps1` to sign both published Windows executables locally, without Azure or a paid certificate.

1. Download and extract the `ReaKiller-Desktop-win-x64` artifact from GitHub Actions onto the Windows PC where you want to sign. Do not sign inside an archive.
2. From PowerShell, run:

```powershell
.\scripts\Sign-ReaKiller.ps1 -BundlePath "C:\ReaKiller" -CreateCertificate
```

The script creates a non-exportable RSA 3072-bit SHA-256 code-signing certificate named `CN=ZH-Systems` in your **CurrentUser Personal** certificate store, signs `ReaKiller.exe` and `ReaKiller.Desktop.exe`, and exports only the **public** certificate (`ZH-Systems-Public-CodeSigning.cer`) to the bundle. On subsequent releases, run the same command without `-CreateCertificate` to reuse the certificate.

To trust this certificate on **your own Windows user profile**, you can use the interactive `-TrustOnThisMachine` flag. It asks for explicit confirmation before importing the public certificate into CurrentUser Trusted Root and Trusted Publishers. Trusting your own self-signed root should only be done on machines you control. For another trusted device, securely confirm the public certificate fingerprint out of band before installing that `.cer` file to the appropriate stores.

To inspect an EXE:

```powershell
Get-AuthenticodeSignature "C:\ReaKiller\ReaKiller.Desktop.exe" | Format-List Status,StatusMessage,SignerCertificate
```

**Limitations:** These signatures are not publicly trusted and may still show **Unknown Publisher** or SmartScreen warnings on unconfigured computers. Creating a self-signed certificate does not earn Windows SmartScreen reputation. The private key stays in the local user certificate store, not GitHub. No free public certificate is created. Keep the signed files together; changing an EXE afterward invalidates its signature. The GitHub Actions artifact remains unsigned unless a separate signing integration is configured.

## PDF compression tab (Ghostscript)

The PDF Compression tab uses the separately installed **Ghostscript** executable (`gswin64c.exe`). It does not bundle or install Ghostscript. Install the 64-bit Windows release manually from https://ghostscript.com/releases/gsdnld.html on machines that need PDF compression. The app locates Ghostscript beneath Program Files or on PATH and displays its status in the PDF tab.

The presets use Ghostscript's `pdfwrite` device: **High quality** (`/printer`), **Balanced** (`/ebook`), and **Smallest file** (`/screen`). These differ in image downsampling and compression; the Balanced preset can dramatically reduce image-heavy PDFs, though the actual reduction depends on each document.

Output is written as `<name>_compressed.pdf` in the selected destination. Source PDFs are preserved, existing outputs are skipped, incomplete temporary files are cleaned up, and the default setting skips new outputs that are not smaller. Rewriting a PDF can affect quality, forms, annotations, metadata and digital signatures, so verify important documents before use. Ghostscript's AGPL/commercial licensing applies; ReaKiller does not redistribute it.

### Compress an entire PDF folder

In the **PDF Compression** tab, select **Choose entire folder** and choose the source directory. **Include PDFs in subfolders** is enabled by default; disable it to process only PDFs immediately inside the selected directory. Choose an output folder and press **Compress PDFs**. The app preserves the source's relative subfolder structure in the destination and writes each result as `<name>_compressed.pdf`.

The previously available **Choose files** picker remains available for individual files or multi-file selection. Source PDFs are never deleted or overwritten. Existing output PDFs are skipped. If the destination is located under the source folder, it is excluded from recursive discovery, including when a prior output already exists. Selecting the source folder itself as the output destination is rejected. Processing continues past individual file errors; progress and per-file statuses are displayed in the PDF results grid.

Folder mode is implemented in the Windows desktop UI, not as a new command-line argument. Windows compilation and a live Ghostscript batch test remain outstanding.

## In-place PDF compression

In the desktop PDF Compression tab, enable **Replace original PDFs in place** to compress every selected PDF directly in its original location, retaining the original filename and folder. This uses a temporary PDF in the same folder; after Ghostscript succeeds and the PDF signature and size have been checked, the application replaces the original using `File.Replace` and a uniquely named rollback backup. Source filesystem creation, modified and accessed timestamps are restored. If **Keep smaller original when compression increases file size** is enabled, the source is left unchanged when the compressed result is not smaller. A confirmation dialog appears before starting. If failure occurs after the replacement, the backup is retained for manual recovery. In-place replacement cannot preserve every embedded PDF property, signature, form, or annotation; retain external backups for important records.

## Parallel PDF batch compression

The PDF Compression tab now offers 1–4 independent Ghostscript workers, defaulting to 2. Multiple PDFs can be compressed concurrently; a single PDF is still processed by one Ghostscript instance and may not finish faster. More workers can reduce throughput on slow disks or when each PDF already saturates CPU or memory. The Stop button cancels the queue and terminates active Ghostscript processes, leaving previously completed PDFs in place. The original source PDF files remain intact.

Compressed PDFs preserve source filesystem creation, modification and access timestamps. Internal PDF metadata timestamps remain separate and are not preserved by this feature. Existing output PDFs are skipped and outputs that are not smaller are skipped when that option is enabled.

## Image output selection and in-place optimization

The Image Conversion tab supports choosing **PNG (lossless)** or **JPEG (lossy)** output. The CLI equivalent is `--format png` or `--format jpg`. JPEG output currently uses encoder quality 90. Conversion into JPEG can reduce file sizes but changes image pixels and must not be mistaken for a lossless medical-image export.

A separate **Optimize PNG in place (same dimensions)** option attempts to recompress existing **.png** files at maximum lossless PNG compression, writing a temporary file and replacing the source only if the result is smaller. It does not resize dimensions or apply enhancement controls. Other formats, including JPEG, DICOM, and TIFF, are skipped in this conservative mode; attempting to optimize JPEG by re-encoding would be lossy and DICOM/TIFF may carry medical metadata. This mode is available via `--optimize-in-place` and intentionally cannot be combined with resizing or enhancement flags.

Source filesystem timestamps are restored after successful replacement. File-level metadata (for example extended attributes, security ACLs, application-specific chunks or profiles) and byte-for-byte pixel equivalence are not yet covered by a dedicated optimization regression test; test on copies before using on sensitive records. Optimization may not reduce already optimized PNGs. The original remains untouched when the new file is not smaller.


## Adaptive conversion concurrency

The image conversion CLI accepts `--adaptive-workers` and the desktop UI has an **Automatically adjust workers for system load** checkbox (selected by default). `--threads N` remains the absolute upper bound, and individual files are admitted according to a dynamic worker limit. The limiter samples ReaKiller **process-level CPU** (normalized by logical processor count) and .NET GC-reported memory pressure approximately every two seconds as workers request permits. It reduces allowed concurrency above 88% process CPU or under elevated GC memory pressure, increases it below 65% process CPU and otherwise holds steady. The implementation does not sample disk latency, actual available physical RAM directly, or total machine-wide CPU use; it should be tuned against migration workloads before being relied on for SQL Server/IIS resource protection. Existing conversions finish naturally when the limit falls.

## Carestream Dental image formats

Carestream CS Imaging's documented portable exports include DICOM, JPEG, TIFF, PNG and BMP. Legacy Carestream orthodontic documentation also names `.cmp` as an imaging format and `.pcx` as an import format. The converter now accepts `.bmp`, `.pcx`, and `.cmp` in addition to its original formats. A `.cmp` input is **not** assumed decodable: the converter checks for embedded standard JPEG, TIFF, PNG, BMP or DICOM signatures and decodes only recognized underlying formats. Proprietary CMP content remains unsupported unless an actual sample and its specification establish a suitable decoder. For clinical image migrations, exporting DICOM or TIFF through Carestream software is preferable to guessing at proprietary bytes; verify image appearance and metadata.

## Large-batch resource isolation

The desktop image converter automatically uses `--batch-size 500` for large jobs. The CLI can opt in with `--batch-size 500`; omitting it retains the standard single-process behaviour. A batch controller launches a separate ReaKiller process for each batch of source files, then waits for it to exit before starting the next batch. This releases ImageMagick native resources between batches rather than relying on periodic garbage collection. It keeps the source-directory relative path structure, preserves global naming indices and merges per-batch CSV reports. Existing output files are skipped unless overwrite is explicitly enabled. The controller continues to later batches after a failed batch (except a requested stop) and returns a failure exit status. Do not use in-place mode unless you have verified backups. On large dental datasets start with 1–2 workers and inspect failures and output counts; this isolation reduces accumulation across batches but cannot fix a leak that exhausts resources within a single batch.

## Windows MSI installer

GitHub Actions now builds a 64-bit MSI using WiX Toolset 4 in addition to the existing portable desktop bundle. Download the **ReaKiller-MSI-win-x64** workflow artifact and run `ReaKiller-win-x64.msi` with administrator privileges. The installer places the self-contained .NET 10 desktop and CLI binaries in `Program Files\\ReaKiller`, adds a Start Menu shortcut, and registers an application entry for Windows uninstall. Subsequent versions use a stable MSI UpgradeCode for major upgrades (update the package version when publishing a new release). The executable bundle remains available independently.

**Ghostscript is not bundled or installed by the MSI.** To enable PDF compression, install Ghostscript for Windows separately according to its applicable licence. This MSI has no custom actions or network calls and does not modify source images during installation.

## XML to CSV (CLI)

```powershell
ReaKiller.exe xml-to-csv "D:\Exports\Patients.xml" --output "D:\CSV" --record-path "Patients/Patient"
ReaKiller.exe xml-to-csv "D:\Exports" --output "D:\CSV" --recursive --threads 4
```

The converter reads XML twice, discovering all flattened columns before writing UTF-8 CSV. Automatic mode selects the first direct child element of the root as the record type; specify an exact root-relative path for mixed schemas. Namespaces use Clark notation, e.g. `{urn:example}Root/{urn:example}Record`. Repeat `--column` to select and order columns. Attributes use @, nested fields use dots, repeated siblings use the base column for the first value and [2], [3], etc. for subsequent values. Missing values are empty. DTDs and external entities are prohibited. Original files and timestamps are preserved by the shared runner; existing outputs are skipped unless --overwrite is specified. Ctrl+C cancels the job. CSV values are quoted but not modified to neutralize spreadsheet formulas; import untrusted values as text.

This milestone implements one CSV per input and exact record paths, not arbitrary XPath expressions, separate related entity tables, or a desktop preview. The unified desktop integration is deferred to the desktop milestone. Memory usage scales with the largest selected record and discovered column set rather than the entire document.

## PDF to CSV (native text, CLI)

Uses exact PdfPig 0.1.17-alpha-202611230028-4a6b5 and positional row reconstruction. No external service is used.

```powershell
ReaKiller.exe pdf-to-csv "D:\Reports\Billing.pdf" --preview
ReaKiller.exe pdf-to-csv "D:\Reports\Billing.pdf" --output "D:\CSV" --first-page 2 --last-page 5 --boundary 150 --boundary 300 --header ID --header Name --header Amount
ReaKiller.exe pdf-to-csv "D:\Reports" --output "D:\CSV" --recursive --threads 4
```

Automatic mode groups words by vertical position (default tolerance 3 PDF points) and horizontal gaps (default 12 points). Explicit ascending boundaries in PDF points provide stable columns and preserve empty cells. The first extracted row is treated as a header; repeated identical headers at the start of later pages are removed unless --keep-headers is specified. Custom headers replace the first row. Preview returns up to 20 rows as JSON without writing output. A preview is not full-document validation. Unequal automatic row widths and pages with no extractable words fail rather than publishing partial output. Original files and timestamps are preserved by the shared runner. CSV values are quoted but not sanitized for spreadsheet formula execution; import untrusted values as text.

Limitations: this is heuristic extraction of a single consistently formatted table per PDF, not a general table recognition engine. Titles, footers, wrapped rows, rotated text, multiple unrelated tables and sparse or misaligned columns may require manual preprocessing; even equal row widths do not establish semantic correctness. Verify output against source reports before migration. Scanned PDF OCR and interactive WPF preview are later milestones. No patient data is included in tests.

### PDF extraction security

CLI conversion and preview launch a separate process for each PDF, using argument lists rather than shell interpolation. Workers reject elevated Windows administrator tokens, cap input at 256 MiB, document pages at 5,000, and extracted words per page at 200,000. Parent monitors a five-minute deadline, approximately 1 GiB private memory and 256 MiB output, with a 512 MiB managed heap budget and at most four concurrent workers. Preview output is capped at 4 MiB. Limits monitored by polling are not hard OS quotas; the worker is not a filesystem or network sandbox and runs as the invoking user. Direct PdfCsvConverter calls are not isolated; production callers must use the isolated CLI path. Do not process untrusted documents as administrator. Document content is not sent to external services. Preview intentionally returns extracted data to the calling terminal; use only in a trusted environment.

## Local OCR (experimental, explicit opt-in)

Install reviewed Ghostscript 10.08.0 and Tesseract 5.5.4 distributions separately. Use trusted, administrator-protected installation directories and verify distribution provenance, native DLLs and language data before approving hashes. The application checks exact executable versions and configured SHA-256 values, but hashes supplied by the user are not independent authenticity evidence. Do not use arbitrary downloaded binaries or untrusted configuration files.

Create a trusted JSON configuration with properties Ghostscript, GhostscriptSha256, Tesseract, TesseractSha256, Tessdata, EnglishDataSha256 and MinimumConfidence (default 80). Paths must be absolute, hashes must be 64 hexadecimal characters, and Tessdata must contain eng.traineddata. Run:

```powershell
ReaKiller.exe pdf-to-csv "D:\Reports\Scan.pdf" --output "D:\CSV" --ocr-config "C:\Trusted\ocr.json"
```

OCR only runs on pages without extractable native words. English recognition, 300 DPI, one concurrent worker, per-tool 90-second timeout, parent five-minute deadline, 64 MiB intermediate file cap and 768 MiB monitored child memory are used. Any word below the configured confidence threshold rejects the page rather than silently substituting uncertain data. Confidence is not a guarantee of correctness. Native text and OCR share the positional table algorithm. Temporary images and TSV files are deleted on normal completion/failure; abrupt worker termination may leave sensitive files in the current user's temporary directory. Restrict access to that directory and clean abandoned transflux-ocr-* folders. No network service is used; process isolation is not a network/filesystem sandbox. Hard resource quotas, native dependency attestation and full scanned PDF integration testing are release blockers.

## Desktop data conversion workspace

Use the left-side Data conversion navigation item to select XML to CSV or PDF to CSV, choose or drop one input file or folder, select a separate destination, and configure record paths, columns, PDF pages, boundaries and optional reviewed OCR configuration. PDF preview displays up to 20 rows in a read-only grid. The desktop launches the bundled CLI using argument lists; PDF parsing and OCR retain the isolated worker path. Conversion output logs are discarded to avoid displaying patient data; the preview intentionally displays extracted values in memory. Stop terminates the CLI process tree; completed outputs remain and abnormal termination may leave temporary files. Existing image and PDF compression workspaces are retained. XML preview, multi-file selection, persistent job history and detailed progress are not yet implemented. Windows UI smoke testing and existing security release blockers remain required.

## Persistent data conversion queue

Add XML/PDF jobs using Add to queue, then Resume queue. Queue starts paused on each launch; Pause queue stops admission of new jobs, not the active process. Stop cancels the active process and pauses admission. Retry failed, cancelled or interrupted jobs with overwrite removed; existing outputs are skipped, not independently validated. Remove selected deletes queue metadata only. Queue history contains operation, state, attempts and creation date. Windows current-user DPAPI encrypts stored CLI arguments in LocalApplicationData; a file lease excludes other instances and writes replace the prior encrypted snapshot. A running job becomes Interrupted on restart. No automatic restart. Storage is limited to 500 jobs. Same-user processes and administrators are outside the DPAPI threat boundary. Legacy image/PDF compression jobs are not yet managed by this queue. Runtime DPAPI, crash recovery and UI integration tests remain required; child-process orphan containment is a release blocker.

## Resource limits and performance

Windows Job Objects enforce aggregate committed-memory limits and terminate assigned descendants when the last job handle closes. Desktop data CLI: 5 GiB / 20 processes; PDF worker: 2 GiB / 4 processes; OCR tool: 768 MiB / 2 processes. PDF workers wait for admission before parsing. OCR and desktop CLI are assigned immediately after launch, not atomically at creation; this leaves a startup race. Nested job assignment failures abort conversion. These limits are not filesystem/network sandboxing. XML limits: 512 Mi characters per document, 64 levels, 16 Mi characters and 100,000 nodes per record, 4,096 columns, 1 Mi characters per field. XML writes use 64 KiB asynchronous buffers. Oversized files require deliberate preprocessing. No throughput improvement is claimed without benchmark measurements. Legacy pipelines, orphan temp cleanup, full isolation and Windows runtime validation remain release blockers.
