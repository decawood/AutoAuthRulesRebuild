#!/bin/bash
#
# AutoAuth Rules Prototype dev launcher — starts the .NET API plus the Vite dev server.
# Called by the "AutoAuth Rules Prototype Dev.app" wrapper. Can also be run directly.

PROJECT_DIR="$HOME/Cursor Files/AutoAuth Rules Re-Write"
FRONTEND_DIR="$PROJECT_DIR/frontend"
BACKEND_PROJECT="$PROJECT_DIR/backend/AutoAuth.Api"
LOG_FILE="$PROJECT_DIR/autoauth_rules_prototype_dev.log"
BACKEND_PORT=5178
FRONTEND_PORT=5173
FRONTEND_URL="http://127.0.0.1:$FRONTEND_PORT"
BACKEND_HEALTH_URL="http://localhost:$BACKEND_PORT/api/health"

is_port_listening() {
    lsof -nP -iTCP:"$1" -sTCP:LISTEN > /dev/null 2>&1
}

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
echo "=== Dev launch: $(date) ===" >> "$LOG_FILE"

if ! command -v dotnet > /dev/null 2>&1; then
    echo "dotnet was not found on PATH" >> "$LOG_FILE"
    osascript -e 'display alert "AutoAuth Rules Prototype Dev" message ".NET was not found. Install .NET SDK, then try again."'
    exit 1
fi

if ! command -v npm > /dev/null 2>&1; then
    echo "npm was not found on PATH" >> "$LOG_FILE"
    osascript -e 'display alert "AutoAuth Rules Prototype Dev" message "npm was not found. Install Node.js, then try again."'
    exit 1
fi

if ! is_port_listening "$BACKEND_PORT"; then
    echo "Starting .NET API on port $BACKEND_PORT..." >> "$LOG_FILE"
    nohup dotnet run --project "$BACKEND_PROJECT" --urls "http://localhost:$BACKEND_PORT" >> "$LOG_FILE" 2>&1 &
    BACKEND_PID=$!
    echo "Backend PID: $BACKEND_PID" >> "$LOG_FILE"
else
    echo ".NET API already running on :$BACKEND_PORT" >> "$LOG_FILE"
fi

for i in $(seq 1 45); do
    if curl -s "$BACKEND_HEALTH_URL" > /dev/null 2>&1; then
        echo ".NET API ready" >> "$LOG_FILE"
        break
    fi

    if [ "$i" -eq 45 ]; then
        echo ".NET API failed to start within 45s. Check $LOG_FILE" >> "$LOG_FILE"
        osascript -e 'display alert "AutoAuth Rules Prototype Dev" message "The local API did not start. See autoauth_rules_prototype_dev.log for details."'
        exit 1
    fi

    sleep 1
done

cd "$FRONTEND_DIR" || exit 1

if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
    echo "Installing frontend dependencies..." >> "$LOG_FILE"
    npm install >> "$LOG_FILE" 2>&1
    INSTALL_EXIT=$?
    if [ $INSTALL_EXIT -ne 0 ]; then
        echo "npm install failed with exit $INSTALL_EXIT" >> "$LOG_FILE"
        osascript -e 'display alert "AutoAuth Rules Prototype Dev" message "Frontend dependency install failed. See autoauth_rules_prototype_dev.log for details."'
        exit 1
    fi
fi

if ! is_port_listening "$FRONTEND_PORT"; then
    echo "Starting Vite dev server on port $FRONTEND_PORT..." >> "$LOG_FILE"
    nohup npm run dev >> "$LOG_FILE" 2>&1 &
    FRONTEND_PID=$!
    echo "Frontend PID: $FRONTEND_PID" >> "$LOG_FILE"
else
    echo "Vite dev server already running on :$FRONTEND_PORT" >> "$LOG_FILE"
fi

for i in $(seq 1 45); do
    if curl -s "$FRONTEND_URL" > /dev/null 2>&1; then
        echo "Vite dev server ready — opening browser" >> "$LOG_FILE"
        open "$FRONTEND_URL"
        exit 0
    fi

    if [ "$i" -eq 45 ]; then
        echo "Vite dev server failed to start within 45s. Check $LOG_FILE" >> "$LOG_FILE"
        osascript -e 'display alert "AutoAuth Rules Prototype Dev" message "The hot-refresh frontend did not start. See autoauth_rules_prototype_dev.log for details."'
        exit 1
    fi

    sleep 1
done
