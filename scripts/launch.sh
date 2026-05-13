#!/bin/bash
#
# AutoAuth Rules Prototype launcher — builds the React frontend, starts the .NET API, opens the browser.
# Called by the "AutoAuth Rules Prototype.app" wrapper. Can also be run directly.

PROJECT_DIR="$HOME/Cursor Files/AutoAuth Rules Re-Write"
FRONTEND_DIR="$PROJECT_DIR/frontend"
BACKEND_PROJECT="$PROJECT_DIR/backend/AutoAuth.Api"
LOG_FILE="$PROJECT_DIR/autoauth_rules_prototype.log"
PORT=5178

cd "$PROJECT_DIR" || exit 1

# Make Homebrew and nvm-installed tools discoverable when launched from a macOS app.
PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"
if [ -d "$HOME/.nvm/versions/node" ]; then
    LATEST_NODE=$(ls "$HOME/.nvm/versions/node" | tail -1)
    if [ -n "$LATEST_NODE" ]; then
        PATH="$HOME/.nvm/versions/node/$LATEST_NODE/bin:$PATH"
    fi
fi

# Rotate log if it exceeds 5 MB.
if [ -f "$LOG_FILE" ]; then
    LOG_SIZE=$(stat -f%z "$LOG_FILE" 2>/dev/null || stat --printf="%s" "$LOG_FILE" 2>/dev/null || echo 0)
    if [ "$LOG_SIZE" -gt 5242880 ]; then
        mv "$LOG_FILE" "${LOG_FILE}.1"
    fi
fi

echo "" >> "$LOG_FILE"
echo "=== Launch: $(date) ===" >> "$LOG_FILE"

if lsof -ti:$PORT > /dev/null 2>&1; then
    echo "Server already running on :$PORT — opening browser" >> "$LOG_FILE"
    open "http://localhost:$PORT"
    exit 0
fi

if ! command -v dotnet > /dev/null 2>&1; then
    echo "dotnet was not found on PATH" >> "$LOG_FILE"
    osascript -e 'display alert "AutoAuth Rules Prototype" message ".NET was not found. Install .NET SDK, then try again."'
    exit 1
fi

if ! command -v npm > /dev/null 2>&1; then
    echo "npm was not found on PATH" >> "$LOG_FILE"
    osascript -e 'display alert "AutoAuth Rules Prototype" message "npm was not found. Install Node.js, then try again."'
    exit 1
fi

echo "Building frontend..." >> "$LOG_FILE"
cd "$FRONTEND_DIR" || exit 1

if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
    echo "Installing frontend dependencies..." >> "$LOG_FILE"
    npm install >> "$LOG_FILE" 2>&1
    INSTALL_EXIT=$?
    if [ $INSTALL_EXIT -ne 0 ]; then
        echo "npm install failed with exit $INSTALL_EXIT" >> "$LOG_FILE"
        osascript -e 'display alert "AutoAuth Rules Prototype" message "Frontend dependency install failed. See autoauth_rules_prototype.log for details."'
        exit 1
    fi
fi

npm run build >> "$LOG_FILE" 2>&1
BUILD_EXIT=$?
if [ $BUILD_EXIT -ne 0 ]; then
    echo "Frontend build failed with exit $BUILD_EXIT" >> "$LOG_FILE"
    osascript -e 'display alert "AutoAuth Rules Prototype" message "Frontend build failed. See autoauth_rules_prototype.log for details."'
    exit 1
fi

cd "$PROJECT_DIR" || exit 1

echo "Starting .NET API on port $PORT..." >> "$LOG_FILE"
dotnet run --project "$BACKEND_PROJECT" --urls "http://localhost:$PORT" >> "$LOG_FILE" 2>&1 &
SERVER_PID=$!
echo "Server PID: $SERVER_PID" >> "$LOG_FILE"

for i in $(seq 1 45); do
    if curl -s "http://localhost:$PORT/api/health" > /dev/null 2>&1; then
        echo "Server ready — opening browser" >> "$LOG_FILE"
        open "http://localhost:$PORT"
        exit 0
    fi
    sleep 1
done

echo "Server failed to start within 45s. Check $LOG_FILE" >> "$LOG_FILE"
kill "$SERVER_PID" 2>/dev/null
osascript -e 'display alert "AutoAuth Rules Prototype" message "The local server did not start. See autoauth_rules_prototype.log for details."'
exit 1
