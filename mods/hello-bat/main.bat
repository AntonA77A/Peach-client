@echo off
rem Any language can talk to Peach over plain HTTP. curl.exe ships with Windows 10+.
curl.exe -s -X POST "%PEACH_API%/log" -H "Authorization: Bearer %PEACH_TOKEN%" -d "{\"message\":\"Hello from Batch!\"}"
echo.
