---
description: Orka veritabanı, Entity Framework Core, Migration'lar ve Redis Cache kuralları
globs:
  - "Orka.Infrastructure/Data/**/*.cs"
  - "Orka.Infrastructure/Migrations/**/*.cs"
  - "Orka.Core/Entities/**/*.cs"
alwaysApply: false
---

# Veritabanı ve Önbellek Kuralları — EF Core / LocalDB / Redis

## 1. Veritabanı Mimarisi (EF Core)
- **LocalDB Uyumu:** Orka, `(localdb)\mssqllocaldb` kullanır. Yazdığınız LINQ sorguları SQL Server provider'ına uygun olmalıdır (Örn: client-side evaluation hatasına sebep olacak C# kodları yazmaktan kaçının).
- **Asenkron İşlemler:** EF Core tarafında `ToList()`, `FirstOrDefault()` YASAKTIR. Sadece `ToListAsync()`, `FirstOrDefaultAsync()` kullanılacaktır.
- **N+1 Problemi:** Bağımlı verileri (örneğin kullanıcının kayıtlı olduğu sınıflar) çekerken mutlaka `.Include()` kullanılmalıdır.
- **Tracking:** Sadece okuma (Read-Only) yapılan sorgularda performansı artırmak için daima `.AsNoTracking()` eklenmelidir.

## 2. Migration Disiplini
- Yeni bir Entity eklediğinde veya değiştirdiğinde, her zaman önce `Orka.Core/Entities` güncellenir. Sonra `Orka.Infrastructure` içinden `dotnet ef migrations add <Isim> --startup-project ../Orka.API` komutu çalıştırılır.
- Kodları manuel `DROP TABLE` veya raw SQL ile bozmayın, her şey Code-First ilerler.

## 3. Önbellek (Redis) Kuralları
- Token/Oturum durumları ve LLM Telemetrisi, Redis üzerinden asenkron yönetilir (`RedisMemoryService.cs`).
- Redis key'lerinde her zaman `orka:` öneki (prefix) zorunludur. (Örn: `orka:session:123`, `orka:metrics:TutorAgent`).
