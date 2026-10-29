MAUI persistent application data

The default SQLite metadata database is created here as maui.db during Initial Setup. This folder is outside wwwroot and is not served as static web content.

The database contains MAUI-owned application state including metadata, Tags, preferences, saved views, Undo/Recycle Bin data, Reports, System Job state, Host Health, and MAUI Admin/User authorization.

Administrators may configure an absolute SQLite path instead. Database files (*.db, *.db-shm, *.db-wal) are intentionally excluded from normal MAUI publish output so application deployment does not replace local runtime data.

Runtime backups are created under Data\Backups. A completed backup contains the Server Key-protected MOVEit configuration, appsettings.json, and, when SQLite is used, a consistent database snapshot.

Do not treat this directory as disposable deployment content.
