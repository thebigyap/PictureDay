# Changelog

All notable changes to PictureDay will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.3.0] - 2026-01-07

### Added

-   **Enhanced Photo Gallery**: Major improvements to the gallery experience
-   **Dynamic Date Range**: Photo Gallery now automatically adjusts to show only months/years with photos
-   **Smart Month Selection**: Month dropdown only displays valid months for the selected year (no future months or empty months)
-   **Statistics Display**: Added eye-catching stats boxes showing Total Photos, Storage Used, and Longest Streak
-   **Open Photos Button**: Quick access button to open the photos folder in File Explorer
-   **No Photos Indicator**: Clear "No Photos" message displayed when viewing months with no screenshots

### Changed

-   Stats and gallery filtering now properly exclude backup and unofficial screenshots

## [2.2.0]

### Fixed

-   **Update System**: Completely overhauled the update mechanism for reliability
-   Fixed update service connection issues that caused "Looking for updates..." to hang indefinitely
-   Improved error handling and connection retry logic for update checks
-   Fixed updater executable not being copied during build process
-   **Update Window UI**: Added missing OK button on download complete screen
-   Fixed Download button remaining visible and clickable during download process
-   **Update Process**: Fixed critical bug where updates weren't being applied correctly
-   Updater now properly waits for extraction and script creation before PictureDay shuts down
-   Improved batch script error handling to detect and report update failures
-   Fixed updater being killed on normal exit (now only killed when not updating)
-   Update process now properly completes before application restart

## [2.1.0]

### Fixed

-   **Scheduled Time Daily Reset**: Scheduled times now properly update each day instead of getting stuck
-   Added `ScheduledTimeDate` tracking to ensure scheduled times are recalculated daily
-   **"Already Passed" Logic for Late-Night Times**: Corrected display logic for times after midnight
-   Times after midnight (before 9 AM) are now correctly recognized as being for the next day
-   Display now checks against the full scheduled window (time + 2.5 minutes) instead of just the exact time
-   Improved time comparison logic to properly handle day boundaries
-   **Arrow Character Encoding**: Fixed corrupted Unicode characters in photo gallery navigation arrows
-   Left and right navigation arrows in image viewer now display correctly (← and →)

## [2.0.0]

### Added

-   **Auto-Update System**: Integrated automatic update checking and installation
-   PictureDay now checks for updates on startup (after 3 second delay)
-   Manual "Check for Updates" option in system tray menu
-   Update window shows available versions and download progress
-   Seamless update process: downloads, applies, and restarts automatically
-   Update service runs in separate process for reliability

## [1.9.0]

### Added

-   "Desktop Only" screenshot mode
-   When enabled, automatically minimizes all windows before capturing
-   Captures only the desktop background without any visible windows
-   Automatically restores all windows after screenshot is taken
-   Perfect for capturing clean desktop wallpapers and backgrounds

## [1.8.0]

### Fixed

-   Windows startup registry to use .exe file instead of .dll
-   Console window now only appears in debug builds (not in release builds)

### Changed

-   Refactored debug console code for better maintainability and cleaner codebase
-   Reordered system tray context menu with "Show PictureDay" as the topmost item
-   Improved user experience for production releases

## [1.7.0]

### Added

-   Extended random time range from 9 AM - 9 PM to 9 AM - 3 AM (next day) for late-night users
-   Intelligent backup screenshot system for late-night scheduled times
-   If a late-night time (9 PM - 3 AM) is selected, automatically schedules a backup screenshot between 9 AM - 9 PM
-   If user is online during late-night time, main screenshot is taken and backup is deleted
-   If user is not online during late-night time, backup screenshot is automatically promoted to main

### Changed

-   Improved screenshot capture reliability for gamers and night owls

## [1.6.0]

### Fixed

-   Image viewer now uses dark background in dark mode (was white)
-   Zoom level text now uses theme colors for proper visibility

### Added

-   Reset button to zoom controls for quick return to 100% zoom

### Changed

-   Improved dark theme consistency across all UI elements

## [1.5.0]

### Added

-   Scheduled time display in settings page
-   Shows today's scheduled screenshot time in 12-hour format (e.g., "2:30 PM")
-   Displays notification if scheduled time has already passed (will schedule for tomorrow)
-   Updates automatically when schedule settings are changed

### Changed

-   Improves user awareness of when screenshots will be captured

## [1.4.0]

### Added

-   Enhanced image viewer with advanced zoom and pan capabilities
-   Zoom controls with +/- buttons and real-time zoom level display (10% to 1000%)
-   Mouse wheel zoom that smoothly zooms toward cursor position
-   Click-and-drag panning for zoomed images with hand cursor indicator
-   Smart zooming that keeps the point under cursor fixed during zoom operations
-   Exit button to image viewer for quick window closure

### Changed

-   Improved image navigation experience with intuitive zoom and pan controls

## [1.3.0]

### Added

-   Context menu to photo gallery images with right-click functionality
-   Context menu options: Open, Open in File Explorer, Copy Image, Copy File Location, Delete
-   Scrolling support to settings view for better accessibility

## [1.2.2]

### Changed

-   Implemented smart time selection for screenshot scheduling
-   Random time selection now respects current time (won't schedule times in the past)
-   If app starts at 3 PM, screenshots will only be scheduled between 3:01 PM and 9:00 PM
-   Improved scheduling logic for both Random and TimeRange modes

## [1.2.1]

### Added

-   Image viewer navigation arrows (left/right) to browse through images in the gallery
-   Navigation arrows automatically hide when there are no more images in that direction

### Changed

-   Improved image viewer user experience with intuitive navigation controls

## [1.2.0]

### Changed

-   Improved error handling and logging for configuration saving
-   Enhanced theme application consistency across all UI elements

### Added

-   Success notification when settings are saved

## [1.1.3]

### Added

-   Dark/Light theme support with comprehensive UI theming

### Changed

-   Improved gallery layout (3 images per row, better sizing)
-   Enhanced UI design with auto-sizing buttons
-   Made window non-resizable
-   Improved dark theme styling for all controls

## [1.1.1]

### Fixed

-   Image viewer to display full-resolution images instead of blurry thumbnails

### Changed

-   Improved image viewer to fit images within preview container

## [1.1.0]

### Added

-   Manual screenshot trigger (button, system tray menu, keyboard shortcuts)
-   Scheduled time customization (fixed time, time range, or random)
-   Screenshot quality/format settings (JPEG/PNG selection, quality slider)
-   Multi-monitor support (capture all monitors or select specific monitor)

## [1.0.0]

### Added

-   Initial release
-   Automatic daily screenshots
-   Privacy filtering
-   Photo gallery
-   Basic configuration
