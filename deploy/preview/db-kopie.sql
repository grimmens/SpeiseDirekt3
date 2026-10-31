-- Kopiert die Produktionsdatenbank auf die PR-Preview-Datenbank (gleiche Instanz).
-- Läuft per sqlcmd mit sa aus ci.yml (Job preview, vom CI-Host gegen server2):
--   sqlcmd -S <server> -U sa -C -b -i deploy/preview/db-kopie.sql \
--     -v Quelle=speisedirekt Ziel=speisedirekt-pr-preview Login=speisedirekt_preview \
--        Pfad=/var/opt/mssql/backup/preview
-- Die logischen Dateinamen der Quelle kommen aus sys.master_files; hat die Quelle mehr als
-- eine Daten- oder Logdatei, bricht das Skript ab, statt still falsch zuzuordnen.
SET NOCOUNT ON;

DECLARE @quelle sysname = N'$(Quelle)', @ziel sysname = N'$(Ziel)', @login sysname = N'$(Login)';
DECLARE @bak nvarchar(400) = N'$(Pfad)/' + @quelle + N'-preview-copy.bak';
DECLARE @sql nvarchar(max), @dataName sysname, @logName sysname;

IF (SELECT COUNT(*) FROM sys.master_files WHERE database_id = DB_ID(@quelle) AND type = 0) <> 1
   OR (SELECT COUNT(*) FROM sys.master_files WHERE database_id = DB_ID(@quelle) AND type = 1) <> 1
    THROW 50000, N'Quelle hat nicht genau eine Daten- und eine Logdatei', 1;
SELECT @dataName = name FROM sys.master_files WHERE database_id = DB_ID(@quelle) AND type = 0;
SELECT @logName  = name FROM sys.master_files WHERE database_id = DB_ID(@quelle) AND type = 1;

-- 1) Sicherung der Quelle. COPY_ONLY: berührt keine Backup-Kette, INIT überschreibt
--    die Datei des letzten Laufs.
SET @sql = N'BACKUP DATABASE ' + QUOTENAME(@quelle) + N' TO DISK = ' + QUOTENAME(@bak, '''')
         + N' WITH COPY_ONLY, INIT, FORMAT, CHECKSUM;';
EXEC sp_executesql @sql;
RESTORE VERIFYONLY FROM DISK = @bak WITH CHECKSUM;
PRINT N'Sicherung ok: ' + @bak;

-- 2) Ziel freiräumen: laufende Verbindungen beenden (die Preview-App ist zu diesem
--    Zeitpunkt gestoppt, das ist nur das Netz).
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @ziel AND state_desc = N'ONLINE')
BEGIN
    SET @sql = N'ALTER DATABASE ' + QUOTENAME(@ziel) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE;';
    EXEC sp_executesql @sql;
END;

-- 3) Wiederherstellen unter neuem Namen und neuen Dateien.
SET @sql = N'RESTORE DATABASE ' + QUOTENAME(@ziel) + N' FROM DISK = ' + QUOTENAME(@bak, '''')
         + N' WITH REPLACE, RECOVERY, CHECKSUM,'
         + N' MOVE ' + QUOTENAME(@dataName, '''') + N' TO N''/var/opt/mssql/data/' + @ziel + N'.mdf'','
         + N' MOVE ' + QUOTENAME(@logName, '''')  + N' TO N''/var/opt/mssql/data/' + @ziel + N'_log.ldf'';';
EXEC sp_executesql @sql;

-- 4) Betrieb: Mehrbenutzer, SIMPLE (keine Log-Backups für eine Preview), kein AUTO_CLOSE,
--    Besitzer ist der Preview-Login — die App verbindet sich nie mit sa.
SET @sql = N'ALTER DATABASE ' + QUOTENAME(@ziel) + N' SET MULTI_USER;'
         + N'ALTER DATABASE ' + QUOTENAME(@ziel) + N' SET RECOVERY SIMPLE;'
         + N'ALTER DATABASE ' + QUOTENAME(@ziel) + N' SET AUTO_CLOSE OFF;'
         + N'ALTER AUTHORIZATION ON DATABASE::' + QUOTENAME(@ziel) + N' TO ' + QUOTENAME(@login) + N';';
EXEC sp_executesql @sql;
PRINT N'Wiederhergestellt: ' + @ziel + N' (Besitzer ' + @login + N')';
GO

-- 5) Stand der Kopie für den Kommentar im PR: Zeitpunkt | Anzahl Migrationen | letzte Migration.
--    (SpeiseDirekt versendet keine Mails und hat keine Warteschlangen — nichts zu bereinigen.)
USE [$(Ziel)];
SELECT CONVERT(nvarchar(19), SYSUTCDATETIME(), 120) + N' UTC' AS KopieUm,
       (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS Migrationen,
       (SELECT TOP 1 MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC) AS LetzteMigration;
GO
