---
description: Orka veritabanı, Entity Framework Core, Migration'lar ve Redis Cache kuralları
globs:
  - "Orka.Infrastructure/Data/**/*.cs"
  - "Orka.Infrastructure/Migrations/**/*.cs"
  - "Orka.Core/Entities/**/*.cs"
alwaysApply: false
---

# Veritabanı ve Önbellek Kuralları — EF Core / LocalDB / Redis

## 1. Mimari Kısıtlamalar
- **Veritabanı:** `(localdb)\mssqllocaldb` üzerinden SQL Server kullanılmaktadır. Sadece Windows/LocalDB uyumlu SQL syntax'ları geçerlidir.
- **ORM:** Entity Framework Core 8 kullanılmaktadır. Linq sorguları optimize edilmeli ve gereksiz "N+1" sorgu problemlerinden kaçınılmalıdır.
- **Cache:** Dağıtık önbellek ve LLM Telemetrisi için Redis kullanılmaktadır (`Orka.Infrastructure/Services/RedisMemoryService.cs`).

## 2. Migration Disiplini
- Yeni bir alan veya tablo eklendiğinde `Orka.Core/Entities` güncellenmeli, ardından `Orka.Infrastructure` dizininde şu komut çalıştırılmalıdır:
  `dotnet ef migrations add <Isim> --startup-project ../Orka.API`
- Entity'lere eklenen yeni property'ler olabildiğince `required` veya nullable `?` tipinde açıkça belirtilmelidir.

## 3. Asenkron İşlemler ve DB Context
- Tüm EF Core çağrıları kesinlikle `Async` olmak zorundadır (`ToListAsync()`, `FirstOrDefaultAsync()`).
- `OrkaDbContext` dependency injection ile Scoped olarak gelir. Arka plan işlerinde (Background Services) veya asenkron event'lerde DbContext kullanılacaksa, mutlaka yeni bir Scope yaratılmalıdır (`IServiceScopeFactory.CreateScope()`).
