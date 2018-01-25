@ECHO OFF

if [%1]==[] GOTO :usage

ECHO Copying CTST4 from %1 ...
XCOPY /Y %1\bin\T4API.40.dll .
XCOPY /Y /F %1\bin\T4Connection.40.dll .
XCOPY /Y /F %1\bin\T4Definitions.40.dll .
XCOPY /Y /F %1\bin\T4Message.40.dll .
XCOPY /Y /F %1\bin\T4TraceListener.40.dll .
XCOPY /Y /F %1\bin\x64\System.Data.SQLite.40.dll x64
XCOPY /Y /F %1\bin\x64\zlib1.dll x64
XCOPY /Y /F %1\bin\x86\System.Data.SQLite.40.dll x86
XCOPY /Y /F %1\bin\x86\zlib1.dll x86
ECHO Done!
EXIT /B 0

:usage
ECHO USAGE: fetch_ctst4_libs.bat [path-to-cts-t4]
EXIT /B 1
