# Orka LLM Eval (promptfoo)

Orka TutorAgent'ın cevap kalitesini faktual / pedagogy / context boyutlarında
**promptfoo** + LLM-as-judge ile ölçer.

## Hızlı Başlangıç

```bash
cd scripts/llm-eval

# 1) Bağımlılıkları kur
npm install

# 2) Test kullanıcısı oluştur + JWT al (.env.local yazılır)
node prepare-token.mjs

# 3) Groq anahtarını ekle (yargıç LLM için — user-secrets'tan kopyala)
#    cd ../../Orka.API && dotnet user-secrets list | grep Groq
#    sonra: echo "GROQ_API_KEY=gsk_..." >> .env.local

# 4) Tüm senaryoları çalıştır
npx promptfoo eval --env-file .env.local

# 5) Sonuçları tarayıcıda göster
npx promptfoo view
```

## Ön Koşullar
- Backend `http://localhost:5065` adresinde ayakta olmalı.
- Yargıç için `GROQ_API_KEY`. Varsayılan model `llama-3.3-70b-versatile`.
  Anahtarı backend user-secrets'tan al: `cd Orka.API && dotnet user-secrets list`.

## Gate Eşikleri (rules/testing.md)
| Metrik                 | Hedef     | Kritik |
|------------------------|-----------|--------|
| LLMOps avg score       | ≥ 7.0/10  | < 5.0  |
| Primary provider ratio | ≥ 85%     | < 60%  |

Değerlendirmelerde bu eşiklerin altına düşen bir dalga varsa PR commit
edilmemeli — önce TutorAgent / provider zincirine dönülür.

## Yeni Senaryo Eklerken
`promptfooconfig.yaml > tests` altına yeni bir blok ekleyin. En az `factual`
ve `context` boyutlarında `llm-rubric` assert'ı olmalı.

```yaml
- vars:
    query: "Yeni soru..."
  description: "Senaryo açıklaması"
  assert:
    - type: llm-rubric
      value: "Faktüel doğruluk ≥ 4/5"
    - type: llm-rubric
      value: "Konuyla alaka ≥ 4/5"
```
