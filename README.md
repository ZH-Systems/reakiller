# ReaKiller

Personal Windows command-line image converter for IMG, DICOM, JPEG, and TIFF to PNG.

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

Supports extensions: .img, .dcm, .dicom, .jpg, .jpeg, .tif, .tiff. Options include --recursive, --threads N, --overwrite, --dry-run, --max-width, --max-height. Output names include the source extension to avoid collisions (e.g. I11624_img.png). Multipage output has page-number suffixes. Results are logged to reakiller-report.csv. Originals are preserved by default. With `--in-place`, the source file is deleted after validated PNG conversion. Output must be outside the input directory tree.

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

## Image output selection and in-place optimization

The Image Conversion tab supports choosing **PNG (lossless)** or **JPEG (lossy)** output. The CLI equivalent is `--format png` or `--format jpg`. JPEG output currently uses encoder quality 90. Conversion into JPEG can reduce file sizes but changes image pixels and must not be mistaken for a lossless medical-image export.

A separate **Optimize PNG in place (same dimensions)** option attempts to recompress existing **.png** files at maximum lossless PNG compression, writing a temporary file and replacing the source only if the result is smaller. It does not resize dimensions or apply enhancement controls. Other formats, including JPEG, DICOM, and TIFF, are skipped in this conservative mode; attempting to optimize JPEG by re-encoding would be lossy and DICOM/TIFF may carry medical metadata. This mode is available via `--optimize-in-place` and intentionally cannot be combined with resizing or enhancement flags.

Source filesystem timestamps are restored after successful replacement. File-level metadata (for example extended attributes, security ACLs, application-specific chunks or profiles) and byte-for-byte pixel equivalence are not yet covered by a dedicated optimization regression test; test on copies before using on sensitive records. Optimization may not reduce already optimized PNGs. The original remains untouched when the new file is not smaller.


## Adaptive conversion concurrency

The image conversion CLI accepts `--adaptive-workers` and the desktop UI has an **Automatically adjust workers for system load** checkbox (selected by default). `--threads N` remains the absolute upper bound, and individual files are admitted according to a dynamic worker limit. The limiter samples ReaKiller **process-level CPU** (normalized by logical processor count) and .NET GC-reported memory pressure approximately every two seconds as workers request permits. It reduces allowed concurrency above 88% process CPU or under elevated GC memory pressure, increases it below 65% process CPU and otherwise holds steady. The implementation does not sample disk latency, actual available physical RAM directly, or total machine-wide CPU use; it should be tuned against migration workloads before being relied on for SQL Server/IIS resource protection. Existing conversions finish naturally when the limit falls.

## Carestream Dental image formats

Carestream CS Imaging's documented portable exports include DICOM, JPEG, TIFF, PNG and BMP. Legacy Carestream orthodontic documentation also names `.cmp` as an imaging format and `.pcx` as an import format. The converter now accepts `.bmp`, `.pcx`, and `.cmp` in addition to its original formats. A `.cmp` input is **not** assumed decodable: the converter checks for embedded standard JPEG, TIFF, PNG, BMP or DICOM signatures and decodes only recognized underlying formats. Proprietary CMP content remains unsupported unless an actual sample and its specification establish a suitable decoder. For clinical image migrations, exporting DICOM or TIFF through Carestream software is preferable to guessing at proprietary bytes; verify image appearance and metadata.
