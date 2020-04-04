@echo off

if [%BATCH_STARTER_EXECUTE_COMMAND%] == [YES] (
    set BATCH_STARTER_EXECUTE_COMMAND=
    powershell -NoProfile -ExecutionPolicy Bypass -File .\go.ps1 %*
) else (
    set BATCH_STARTER_EXECUTE_COMMAND=YES
    call %0 %* <NUL
)