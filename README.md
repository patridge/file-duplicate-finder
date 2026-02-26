# FileDeduplicator

Offers a system for identifying which files are actually identical to avoid redundant backups, and for identifying non-seen files to find files that are not yet backed up.

## Project Vision

The goal of FileDeduplicator is to scan directories for duplicate files, with the ability to configure identifying duplicates allowing for certain metadata items to differ (such as EXIF in images or ID3 tags in audio). The system should be extensible to support specialized comparison logic for different file types.

## Current Plan

* Running a scan will use a Scanner with all the configuration required to complete the scan.
* A Scanner will use various Comparers to compare file types, with the most basic being just binary file comparison (allowing differences in timestamps to be considered equivalent).
* Comparers will use various Identifiers to determine if a file is of a type that the comparer can handle (image, audio, etc.).
