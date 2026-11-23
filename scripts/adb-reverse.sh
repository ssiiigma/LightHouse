#!/usr/bin/env bash
# Дає емулятору доступ до API через localhost (потрібно для повернення з Google Calendar).
export PATH="$PATH:$HOME/Library/Android/sdk/platform-tools"
adb reverse tcp:5080 tcp:5080 && adb reverse tcp:8080 tcp:8080 && adb reverse --list
