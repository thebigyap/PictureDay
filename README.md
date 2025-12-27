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

-   **Random Time**: Automatically picks a random time between 9 AM and 9 PM (default)
-   **Fixed Time**: Set a specific time for daily screenshots
-   **Time Range**: Random time within a custom range

### Screenshot Settings

-   **Image Format**: Choose between JPEG or PNG
-   **Quality Control**: Adjustable JPEG quality (10-100)
-   **Multi-Monitor Support**: Capture primary monitor, all monitors, or a specific monitor

### Gallery & Management

-   **Photo Gallery**: Browse screenshots organized by month and year
-   **Thumbnail View**: Quick preview of all screenshots
-   **Full-Screen Viewer**: Click any thumbnail to view full-size image

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

See LICENSE

## Contributing

Any contributions are welcome! Please use best coding practices and clearly explain what you added in pull requests!

## Support

If you find a bug, please make an issue report and describe any steps necessary to re-create the issues, if possible. The more information you give, the better I can fix issues.
