# FileDeduplicator

Identifies duplicate files across directories to avoid redundant backups and copies.

## Project Vision

Scan directories for duplicate files, with configurable comparison that can ignore certain metadata (e.g., EXIF in images, ID3 tags in audio). Internally extensible to support specialized comparison logic for different file types at the cost of additional complexity and performance overhead.

## Architecture

* A **Scanner** collects file candidates, groups by size, then hashes size-matched files.
* **Comparers** handle type-specific equivalence (binary, image, audio), with the most basic being full file hash comparison.
* **Identifiers** determine whether a file is a type a given comparer can handle.

## Commands

### `find-duplicates`

Primary command. Scans one or more directories for duplicate files with an interactive results browser.

```shell
deduper find-duplicates --path /path/to/scan
deduper find-duplicates --path /path1 --path /path2 --min-size 500MB
deduper find-duplicates --path /path/to/scan --allow-metadata-diffs
deduper find-duplicates --path /path/to/scan --exclude /path/to/scan/skip-this
```

Options:

* `-p|--path` — Directories to scan (repeatable, defaults to current directory)
* `-x|--exclude` — Subdirectories to skip (repeatable)
* `-s|--min-size` — Minimum file size filter, supports suffixes: KB, MB, GB, TB
* `-m|--allow-metadata-diffs` — Ignore metadata differences (ID3 tags, EXIF data) when comparing

Results browser features:

* Paged list of duplicate groups, sorted by size (default) or path
* Toggle sort order between size and path
* Drill into a group to see matched files with filename, size, and directory
* Open a file's location in Finder/Explorer
* Refresh a match group to re-verify files (removes missing/changed files, drops the group if no duplicates remain)

### `compare`

Compare two specific files for size and hash match.

```shell
deduper compare ./file1.txt ./file2.txt
```
