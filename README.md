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
