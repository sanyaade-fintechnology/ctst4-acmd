# CTS T4 MD/AC connector module

This has been tested against versions of the CTS T4:
- 4.3.75.21 (sim)

## BUILDING

CTS T4 DLL files are not included here for copyright reasons. I'm not sure whether it's illegal to copy them directly here or not so I decided to play safe and not do it.

The project is referencing dlls from lib directory and will copy necessary dlls from there to target directory as necessary.

List of necessary dll files to copy from $CTST4_DIR/bin:
- T4API.40.dll -> lib/T4API.40.dll
- T4Connection.40.dll -> lib/T4Connection.40.dll
- T4Definitions.40.dll -> lib/T4Definitions.40.dll
- T4Message.40.dll -> lib/T4Message.40.dll
- T4TraceListener.40.dll -> lib/T4TraceListener.40.dll
- x86/System.Data.SQLite.40.dll -> lib/x86/System.Data.SQLite.40.dll
- x86/zlib1.dll -> lib/x86/zlib1.dll
- x64/System.Data.SQLite.40.dll -> lib/x64/System.Data.SQLite.40.dll
- x64/zlib1.dll -> lib/x64/zlib1.dll

You can use lib/fetch_ctst4_libs.bat script to copy all the necessary libraries from CTS T4 installation dir.

Open solution in VS2010+, select Debug/Release and platform type (x86/x64) and build.
