-- Orka AI — Admin Promotion Helper
--
-- Önce mevcut kullanıcıları listeler, sonra email'ine göre admin yapar.
-- Kullanım:
--   1) @TargetEmail değerini kendi e-postana göre güncelle.
--   2) sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql

DECLARE @TargetEmail NVARCHAR(200) = N'aakif1345@gmail.com';

PRINT '== Mevcut kullanicilar ==';
SELECT Id, Email, IsAdmin, CreatedAt FROM Users ORDER BY CreatedAt DESC;

PRINT '== Admin guncellemesi ==';
UPDATE Users SET IsAdmin = 1 WHERE Email = @TargetEmail;
SELECT @@ROWCOUNT AS RowsAffected, @TargetEmail AS UpdatedEmail;

PRINT '== Sonuc ==';
SELECT Id, Email, IsAdmin FROM Users WHERE Email = @TargetEmail;
