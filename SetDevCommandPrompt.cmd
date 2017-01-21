set CommonToolsDir=%VS140COMNTOOLS%
if not exist "%CommonToolsDir%" exit /b 1
call "%CommonToolsDir%\VsDevCmd.bat"