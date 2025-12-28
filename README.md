# PictureDay

A Windows desktop application that captures daily screenshots with privacy protection features. I made this because I've recently found some old pictures of my desktop from 4+ years ago and I thought it would be cool and interesting for anyone to be able to track how their computer changes over time.

## Overview

PictureDay takes a screenshot of your screen once per day at a scheduled time. It includes privacy filters to prevent screenshots when sensitive applications are running (or when you're... inspecting... stuff...), and provides a gallery to browse your captured screenshots organized by month.

## Features

### Core Functionality

-   **Automatic Daily Screenshots**: Captures one screenshot per day at a configurable time
-   **Privacy Protection**: Blocks screenshots when blocked applications or private browsing modes are detected
-   **Activity Monitoring**: Only captures screenshots when the user is active
-   **Backup Screenshots**: Takes backup screenshots if the scheduled time is missed
-   **Monthly Organization**: Screenshots are automatically organized into monthly folders

### Manual Controls

-   **Manual Screenshot Trigger**: Take screenshots on-demand via button, system tray menu, or keyboard shortcuts (F12 or Ctrl+S)
-   **System Tray Integration**: Runs in the background with system tray icon for quick access

### Scheduling Options

-   **Random Time**: Automatically picks a random time between 9 AM and 3 AM (next day), with smart selection that ensures times are always in the future
-   **Intelligent Backup System**: For late-night times (9 PM - 3 AM), automatically schedules a backup screenshot during daytime hours (9 AM - 9 PM) to ensure you never miss a day
-   **Fixed Time**: Set a specific time for daily screenshots
-   **Time Range**: Random time within a custom range, with smart selection that respects current time

### Screenshot Settings

-   **Image Format**: Choose between JPEG or PNG
-   **Quality Control**: Adjustable JPEG quality (10-100)
-   **Multi-Monitor Support**: Capture primary monitor, all monitors, or a specific monitor

### Gallery & Management

-   **Photo Gallery**: Browse screenshots organized by month and year
-   **Thumbnail View**: Quick preview of all screenshots
-   **Full-Screen Viewer**: Click any thumbnail to view full-size image with navigation arrows to browse through images
-   **Zoom Controls**: Zoom in/out with mouse wheel or +/- buttons, with zoom level display (10% to 1000%) and reset button
-   **Pan & Drag**: Click and drag to pan around zoomed images with intuitive hand cursor
-   **Smart Zooming**: Zoom centers on cursor position for precise navigation
-   **Context Menu**: Right-click any image for quick actions (Open, Open in File Explorer, Copy Image, Copy File Location, Delete)

## Requirements

-   Windows 10/11
-   .NET 8.0 Runtime
-   At least one monitor

## Installation

1. Download the latest release
2. Extract the files to your desired location
3. Run `PictureDay.exe`
4. The application will start minimized in the system tray

## Usage

### First Run

On first launch, PictureDay will:

-   Create a default screenshot directory in `Pictures\PictureDay`
-   Set up automatic daily screenshots with random scheduling
-   Add itself to Windows startup (optional)

### Taking Manual Screenshots

You can take screenshots manually using:

-   **Button**: Click "Take Screenshot Now" in the main window
-   **System Tray**: Right-click the system tray icon → "Take Screenshot Now"
-   **Keyboard**: Press `F12` or `Ctrl+S` when the main window is focused

### Viewing Screenshots

1. Open PictureDay from the system tray
2. Navigate to the "Photo Gallery" tab
3. Use the month/year dropdowns or arrow buttons to browse
4. Click any thumbnail to view the full-size image
5. Use the left/right arrow buttons in the image viewer to navigate between images
6. **Zoom Controls**: Use mouse wheel to zoom in/out (zooms toward cursor position), or use the +/- buttons with zoom level display. Click "Reset" to return to 100% zoom
7. **Pan & Drag**: When zoomed in, click and drag to pan around the image (cursor changes to hand when panning is available)
8. Right-click any image for context menu options (Open, Open in File Explorer, Copy Image, Copy File Location, Delete)
9. Click "Exit" button to close the image viewer

### Configuration

Access settings via:

-   Main window → "Settings" tab
-   System tray icon → "Settings"

#### Available Settings

**Privacy Filter**

-   Add applications to block list (screenshots won't be taken when these apps are running)
-   Automatically detects private browsing modes

**Screenshot Schedule**

-   Choose scheduling mode (Random, Fixed Time, or Time Range)
-   Configure time settings based on selected mode
-   View today's scheduled screenshot time (displays in 12-hour format)
-   See notification if scheduled time has already passed

**Monitor Selection**

-   Primary Monitor (default)
-   All Monitors (combined capture)
-   Specific Monitor (select from available monitors)

**Quality & Format**

-   Image format: JPEG or PNG
-   JPEG quality slider (10-100, only applies to JPEG)

**Storage**

-   Configure screenshot directory
-   Browse to select custom location

**Startup**

-   Enable/disable "Start with Windows"

## Privacy & Security

-   Screenshots are stored locally on your computer
-   Privacy filter prevents screenshots when sensitive applications are detected
-   No data is sent to external servers
-   All processing happens on your local machine

## File Organization

Screenshots are organized as follows:

```
ScreenshotDirectory/
  ├── 2024-01/
  │   ├── 2024-01-15_14-30-45.jpg
  │   ├── 2024-01-16_09-15-22.jpg
  │   └── ...
  ├── 2024-02/
  │   └── ...
  └── ...
```

## Configuration File

Settings are stored in:

```
%APPDATA%\PictureDay\config.json
```

## Keyboard Shortcuts

-   `F12` - Take screenshot now
-   `Ctrl+S` - Take screenshot now
-   `Ctrl+W` - Close window (minimizes to tray)

## Troubleshooting

### Screenshots Not Being Taken

1. Check that the application is running (look for system tray icon)
2. Verify your schedule settings in Settings
3. Ensure you're active (screenshots only capture when user is active)
4. Check if privacy filter is blocking (blocked apps or private browsing detected)

### Can't Find Screenshots

1. Check the screenshot directory in Settings
2. Verify the directory exists and is accessible
3. Check Windows file permissions

### Application Won't Start

1. Ensure .NET 8.0 Runtime is installed
2. Check Windows Event Viewer for error details
3. Run as administrator if permission issues occur

## Technical Details

-   **Framework**: .NET 8.0
-   **UI**: WPF (Windows Presentation Foundation)
-   **Image Format**: JPEG (default) or PNG
-   **Storage**: Local file system
-   **Architecture**: Service-based with dependency injection

## Development

### Building from Source

```bash
dotnet restore
dotnet build
dotnet run
```

### Project Structure

```
PictureDay/
├── Models/          # Data models (AppConfig, ScreenshotMetadata)
├── Services/        # Core services (Screenshot, Storage, Scheduler, etc.)
├── Views/           # UI views (Settings, PhotoGallery)
├── Utils/           # Utility classes
└── App.xaml.cs      # Application entry point
```

## Version History

### v1.7.0

-   Extended random time range from 9 AM - 9 PM to 9 AM - 3 AM (next day) for late-night users
-   Added intelligent backup screenshot system for late-night scheduled times
-   If a late-night time (9 PM - 3 AM) is selected, automatically schedules a backup screenshot between 9 AM - 9 PM
-   If user is online during late-night time, main screenshot is taken and backup is deleted
-   If user is not online during late-night time, backup screenshot is automatically promoted to main
-   Improved screenshot capture reliability for gamers and night owls

### v1.6.0

-   Image viewer now uses dark background in dark mode (was white)
-   Zoom level text now uses theme colors for proper visibility
-   Added reset button to zoom controls for quick return to 100% zoom
-   Improved dark theme consistency across all UI elements

### v1.5.0

-   Added scheduled time display in settings page
-   Shows today's scheduled screenshot time in 12-hour format (e.g., "2:30 PM")
-   Displays notification if scheduled time has already passed (will schedule for tomorrow)
-   Updates automatically when schedule settings are changed
-   Improves user awareness of when screenshots will be captured

### v1.4.0

-   Enhanced image viewer with advanced zoom and pan capabilities
-   Added zoom controls with +/- buttons and real-time zoom level display (10% to 1000%)
-   Implemented mouse wheel zoom that smoothly zooms toward cursor position
-   Added click-and-drag panning for zoomed images with hand cursor indicator
-   Smart zooming that keeps the point under cursor fixed during zoom operations
-   Added exit button to image viewer for quick window closure
-   Improved image navigation experience with intuitive zoom and pan controls

### v1.3.0

-   Added context menu to photo gallery images with right-click functionality
-   Context menu options: Open, Open in File Explorer, Copy Image, Copy File Location, Delete
-   All context menu actions are fully functional
-   Added scrolling support to settings view for better accessibility

### v1.2.2

-   Implemented smart time selection for screenshot scheduling
-   Random time selection now respects current time (won't schedule times in the past)
-   If app starts at 3 PM, screenshots will only be scheduled between 3:01 PM and 9:00 PM
-   Improved scheduling logic for both Random and TimeRange modes

### v1.2.1

-   Added image viewer navigation arrows (left/right) to browse through images in the gallery
-   Navigation arrows automatically hide when there are no more images in that direction
-   Improved image viewer user experience with intuitive navigation controls

### v1.2.0

-   Improved error handling and logging for configuration saving
-   Enhanced theme application consistency across all UI elements
-   Added success notification when settings are saved

### v1.1.3

-   Added Dark/Light theme support with comprehensive UI theming
-   Improved gallery layout (3 images per row, better sizing)
-   Enhanced UI design with auto-sizing buttons
-   Made window non-resizable
-   Improved dark theme styling for all controls

### v1.1.1

-   Fixed image viewer to display full-resolution images instead of blurry thumbnails
-   Improved image viewer to fit images within preview container

### v1.1.0

-   Manual screenshot trigger (button, system tray menu, keyboard shortcuts)
-   Scheduled time customization (fixed time, time range, or random)
-   Screenshot quality/format settings (JPEG/PNG selection, quality slider)
-   Multi-monitor support (capture all monitors or select specific monitor)

### v1.0.0

-   Initial release
-   Automatic daily screenshots
-   Privacy filtering
-   Photo gallery
-   Basic configuration

## License

See [LICENSE](LICENSE)

## Contributing

Any contributions are welcome! Please use best coding practices and clearly explain what you added in pull requests!

## Support

If you find a bug, please make an issue report and describe any steps necessary to re-create the issues, if possible. The more information you give, the better I can fix issues.
