#!/bin/bash
#
# Creates the "AutoAuth Rules Prototype Dev.app" macOS application.
# Run this once. The .app is placed on the Desktop for easy Dock dragging.

PROJECT_DIR="$HOME/Cursor Files/AutoAuth Rules Re-Write"
APP_NAME="AutoAuth Rules Prototype Dev"
APP_PATH="$HOME/Desktop/$APP_NAME.app"

rm -rf "$APP_PATH"

osacompile -o "$APP_PATH" <<'APPLESCRIPT'
on run
    set projectDir to (POSIX path of (path to home folder)) & "Cursor Files/AutoAuth Rules Re-Write"
    set launchScript to quoted form of (projectDir & "/scripts/launch-dev.sh")
    do shell script launchScript & " &> /dev/null &"
end run
APPLESCRIPT

if [ -d "$APP_PATH" ]; then
    echo "Created: $APP_PATH"
    echo ""
    echo "To add to your Dock:"
    echo "1. Find 'AutoAuth Rules Prototype Dev' on your Desktop"
    echo "2. Drag it onto your Dock"
    echo "3. Click it to launch the hot-refresh prototype"
    echo ""
    echo "The dev app starts the .NET API on port 5178, starts Vite on port 5173, and opens http://127.0.0.1:5173."
else
    echo "Failed to create dev app. Check for errors above."
    exit 1
fi
