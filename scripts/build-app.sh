#!/bin/bash
#
# Creates the "AutoAuth Rules Prototype.app" macOS application.
# Run this once. The .app is placed on the Desktop for easy Dock dragging.

PROJECT_DIR="$HOME/Cursor Files/AutoAuth Rules Re-Write"
APP_NAME="AutoAuth Rules Prototype"
APP_PATH="$HOME/Desktop/$APP_NAME.app"

rm -rf "$APP_PATH"

osacompile -o "$APP_PATH" <<'APPLESCRIPT'
on run
    set projectDir to (POSIX path of (path to home folder)) & "Cursor Files/AutoAuth Rules Re-Write"
    set launchScript to quoted form of (projectDir & "/scripts/launch.sh")
    do shell script launchScript & " &> /dev/null &"
end run
APPLESCRIPT

if [ -d "$APP_PATH" ]; then
    echo "Created: $APP_PATH"
    echo ""
    echo "To add to your Dock:"
    echo "1. Find 'AutoAuth Rules Prototype' on your Desktop"
    echo "2. Drag it onto your Dock"
    echo "3. Click it to launch the prototype"
    echo ""
    echo "The app builds the frontend, starts the .NET API on port 5178, and opens your browser automatically."
else
    echo "Failed to create app. Check for errors above."
    exit 1
fi
