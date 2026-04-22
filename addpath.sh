#!/bin/bash

# Script to add the linux-x64 folder to PATH permanently

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
LINUX_X64_PATH="$PROJECT_DIR/bin/Release/net8.0/linux-x64"

if [ ! -d "$LINUX_X64_PATH" ]; then
    echo "Error: Directory $LINUX_X64_PATH does not exist"
    exit 1
fi

# Check if already in PATH
if grep -q "$LINUX_X64_PATH" ~/.bashrc; then
    echo "Path already exists in ~/.bashrc"
    exit 0
fi

echo "export PATH=\"\$PATH:$LINUX_X64_PATH\"" >> ~/.bashrc
echo "Added $LINUX_X64_PATH to ~/.bashrc"
echo "Run 'source ~/.bashrc' to apply changes to current session"
