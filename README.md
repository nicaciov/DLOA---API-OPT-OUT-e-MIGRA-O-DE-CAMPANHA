# InterplayersGateway

Gateway ASP.NET Core 8 que encapsula as APIs da plataforma **Interplayers Loyalty** para o cliente DLOA.

Desenvolvido por **Nicacio INC**.

---

## Visão Geral

| Tarefa | Endpoint | Descrição |
|--------|----------|-----------|
| Tarefa 1 | `PATCH /api/optin/opt-out` | Opt-out de canais (LGPD) |
| Tarefa 2 | `PATCH /api/campaign/migrate` | Migração de campanha (segmentação PDV) |

---

## 📋 Documentação de Uso - API OPT-OUT

### **Endpoint**
PATCH http://35.174.242.254:84/api/OptIn/opt-out

### **Headers Obrigatórios**
Content-Type: application/json
Accept: application/json

---

## **Campos do Payload**

| Campo | Tipo | Obrigatório | Descrição | Exemplo |
|-------|------|-------------|-----------|---------|
| **consumerId** | string | ⚠️ Condicional | ID externo do consumidor no Logix (IdCons). Ao menos um de: `consumerId`, `phone` ou `email` deve ser informado. | `"75712547"` |
| **phone** | string | ⚠️ Condicional | Telefone do paciente para lookup (alternativa ao consumerId) | `"11987654321"` |
| **email** | string | ⚠️ Condicional | E-mail do paciente para lookup (alternativa ao consumerId) | `"paciente@example.com"` |
| **optOutType** | string | ✅ **Obrigatório** | Tipo de opt-out: `"channel_opt_out"` ou `"global_opt_out"` | `"channel_opt_out"` |
| **channel** | string | ⚠️ Condicional | Canal afetado **quando** `optOutType = "channel_opt_out"`. Valores: `"whatsapp"` \| `"email"` \| `"sms"` \| `"phone"` \| `"push"` \| `"mail"` | `"whatsapp"` |
| **origin** | string | ✅ **Obrigatório** | Origem da solicitação (auditoria LGPD). Valores esperados: `"WPP"` \| `"EMAIL"` \| `"PDV"` \| `"MANUAL"` ou custom | `"DLOA AI"` |
| **idempotencyKey** | string | ❌ Opcional | Chave para garantir idempotência. Recomendado usar header `Idempotency-Key` em vez deste campo | `"550e8400-e29b-41d4-a716-446655440000"` |

---

## **Regras de Negócio**

### **1️⃣ channel_opt_out (desabilitar canal específico)**
{
  "consumerId": "75712547",
  "optOutType": "channel_opt_out",
  "channel": "whatsapp",
  "origin": "DLOA AI"
}
**Resultado**: Apenas WhatsApp é desabilitado ("N"), demais canais (email, sms, phone, push, mail) mantêm seu estado atual.

---

### **2️⃣ global_opt_out (desabilitar todos os canais)**
{
  "consumerId": "75712547",
  "optOutType": "global_opt_out",
  "origin": "MANUAL"
}
**Resultado**: TODOS os canais são desabilitados ("N"): WhatsApp, email, sms, phone, push, mail.

⚠️ **Nota**: Campo `channel` é **ignorado** quando `optOutType = "global_opt_out"`.

---

### **3️⃣ Lookup por Telefone**
{
  "phone": "11987654321",
  "optOutType": "channel_opt_out",
  "channel": "email",
  "origin": "EMAIL"
}

---

### **4️⃣ Idempotência (evitar reprocessamento)**
{
  "consumerId": "75712547",
  "optOutType": "channel_opt_out",
  "channel": "whatsapp",
  "origin": "WPP",
  "idempotencyKey": "550e8400-e29b-41d4-a716-446655440000"
}
Ou via header:
curl -X 'PATCH' \
  'http://35.174.242.254:84/api/OptIn/opt-out' \
  -H 'Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000' \
  -H 'Content-Type: application/json' \
  -d '{"consumerId": "75712547", ...}'

---

## **Respostas**

### ✅ **Sucesso (200)**
{
  "description": "Opt-out processado com sucesso.",
  "idempotencyKey": "550e8400-e29b-41d4-a716-446655440000"
}

### ❌ **Erro 400 - Dados Inválidos**
{
  "error": "OptOutType é obrigatório.",
  "details": null,
  "statusCode": 400,
  "occurredAt": "2026-04-28T20:16:24Z"
}

### ❌ **Erro 404 - Consumidor Não Encontrado**
{
  "error": "Consumidor não encontrado na base Interplayers.",
  "details": null,
  "statusCode": 404,
  "occurredAt": "2026-04-28T20:16:24Z"
}

### ❌ **Erro 502 - Falha na Interplayers**

{
  "error": "Erro ao atualizar preferências: Erro desconhecido. (UNKNOWN)",
  "details": null,
  "statusCode": 502,
  "occurredAt": "2026-04-28T20:16:24Z"
}

---

## **Exemplos cURL Práticos**

### **Exemplo 1: Opt-out WhatsApp**
curl -X 'PATCH' \
  'http://35.174.242.254:84/api/OptIn/opt-out' \
  -H 'Content-Type: application/json' \
  -d '{
    "consumerId": "75712547",
    "optOutType": "channel_opt_out",
    "channel": "whatsapp",
    "origin": "DLOA AI"
  }'

### **Exemplo 2: Opt-out Global (todos os canais)**
curl -X 'PATCH' \
  'http://35.174.242.254:84/api/OptIn/opt-out' \
  -H 'Content-Type: application/json' \
  -d '{
    "consumerId": "75712547",
    "optOutType": "global_opt_out",
    "origin": "MANUAL"
  }'

### **Exemplo 3: Com Idempotência (header)**
curl -X 'PATCH' \
  'http://35.174.242.254:84/api/OptIn/opt-out' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: req-20260428-001' \
  -d '{
    "consumerId": "75712547",
    "optOutType": "channel_opt_out",
    "channel": "email",
    "origin": "EMAIL"
  }'

### **Exemplo 4: Opt-out por Email**
curl -X 'PATCH' \
  'http://35.174.242.254:84/api/OptIn/opt-out' \
  -H 'Content-Type: application/json' \
  -d '{
    "email": "paciente@example.com",
    "optOutType": "channel_opt_out",
    "channel": "sms",
    "origin": "EMAIL"
  }'

---

## Pré-requisitos

- .NET 8 SDK
- Acesso à rede para os endpoints da Interplayers (homologação/produção)

---

## Configuração

### appsettings.json

{
  "Interplayers": {
    "TokenUrl": "https://interplayersdevb2c.b2clogin.com/.../token",
    "ClientId": "...",
    "ClientSecret": "...",
    "Scope": "https://interplayersdevb2c.onmicrosoft.com/services/loyalty-api/.default",
    "BaseUrlRegistration": "https://idp-api-gtw.azure-api.net/external-loyalty-registration-pre",
    "BaseUrlAdhesion": "https://idp-api-gtw.azure-api.net/external-loyalty-adhesion-pre",
    "AdministratorId": "57"
  },
  "Gateway": {
    "ApiKey": "sua-chave-aqui",
    "IdempotencyTtlMinutes": 60
  },
  "Audit": {
    "LogPath": "logs/audit-.log"
  }
}

Para produção, use **variáveis de ambiente** ou **Secrets Manager** — nunca versione credenciais no appsettings.

---

## Executando localmente

cd InterplayersGateway
dotnet restore
dotnet run

Swagger disponível em: `http://localhost:5000/swagger`

---

## Endpoints

### Tarefa 1 — Opt-out

PATCH /api/optin/opt-out
X-Api-Key: sua-chave
Content-Type: application/json
Idempotency-Key: uuid-unico-opcional

#### channel_opt_out — WhatsApp

{
  "consumerId": "5794636008",
  "optOutType": "channel_opt_out",
  "channel": "whatsapp",
  "origin": "WPP"
}

**Resultado no Logix:** `whatsApp = N` | email mantém valor atual

#### channel_opt_out — E-mail

{
  "consumerId": "5794636008",
  "optOutType": "channel_opt_out",
  "channel": "email",
  "origin": "EMAIL"
}

**Resultado no Logix:** `email = N` | whatsApp mantém valor atual

#### global_opt_out

{
  "consumerId": "5794636008",
  "optOutType": "global_opt_out",
  "origin": "WPP"
}

**Resultado no Logix:** todos os canais = N (whatsApp, email, sms, phone, push, mail)

#### Identificação por telefone ou e-mail

{
  "phone": "11999998888",
  "optOutType": "channel_opt_out",
  "channel": "whatsapp",
  "origin": "WPP"
}

> **Atenção:** lookup por phone/email depende de endpoint da Interplayers ou base local.
> Ver `OptInService.ResolveConsumerId()` para implementar.

---

### Tarefa 2 — Migração de Campanha

PATCH /api/campaign/migrate
X-Api-Key: sua-chave
Content-Type: application/json
Idempotency-Key: uuid-unico-opcional

{
  "consumerId": "5794636008",
  "ean": "7896015523398",
  "newCampaignId": "8",
  "indication": "Indicação A",
  "origin": "WPP_FLOW"
}

**Mapeamento indicação → campaignId** (a definir com o time de Atendimento):

| Resposta do paciente | newCampaignId |
|---------------------|---------------|
| 1 - Indicação A     | 8             |
| 2 - Indicação B     | 9             |
| 3 - Indicação C     | 10            |

---

## Idempotência

Envie o header `Idempotency-Key` com um UUID único por operação.
Requisições repetidas com a mesma chave retornam 200 sem reprocessar.

Idempotency-Key: 3f2504e0-4f89-11d3-9a0c-0305e82c3301

TTL configurável em `Gateway:IdempotencyTtlMinutes` (padrão: 60 min).

> **Para produção:** substituir `IMemoryCache` por Redis em `IdempotencyService.cs`.

---

## Auditoria LGPD

Toda operação de opt-out e migração gera entrada em:
- **Log estruturado Serilog** (console + arquivo `logs/app-YYYYMMDD.log`)
- **Arquivo de auditoria dedicado** `logs/audit-YYYYMMDD.log` (JSONL, uma entrada por linha)

Campos gravados: ConsumerId, origem, tipo de ação, canal, timestamp UTC, IP, sucesso/erro, código de erro Interplayers.

---

## Códigos de erro Interplayers

| Código | Significado |
|--------|-------------|
| LR04   | Consumidor não encontrado |
| MP76   | Campanha antiga não localizada |
| MP77   | Campanha nova não localizada |
| L000   | Erro interno Interplayers |

---

## Estrutura do projeto

InterplayersGateway/
├── Controllers/
│   ├── OptInController.cs         ← Tarefa 1
│   └── CampaignController.cs      ← Tarefa 2
├── Services/
│   ├── InterplayersAuthService.cs ← OAuth 2.0 + cache de token
│   ├── OptInService.cs            ← regras de negócio opt-out
│   ├── CampaignMigrationService.cs← migração de campanha
│   └── ServiceResult.cs           ← padrão de retorno
├── Models/
│   ├── AuditEntry.cs
│   ├── Requests/Requests.cs
│   └── Responses/Responses.cs
├── Infrastructure/
│   ├── AuditLogger.cs             ← LGPD
│   └── IdempotencyService.cs      ← idempotência
├── Middleware/
│   └── ApiKeyMiddleware.cs        ← autenticação do gateway
├── Program.cs
└── appsettings.json

---

## Pendências (gaps identificados)

1. **Lookup por telefone/e-mail:** confirmar com Interplayers se existe endpoint de busca de consumidor por phone/email, ou implementar via base local DLOA.
2. **Tabela indicação → campaignId:** definir com o time de Atendimento/Soluções os IDs das campanhas por indicação.
3. **Identificação dos pacientes PDV sem indicação:** definir query de segmentação para disparar a pesquisa proativa.
4. **Idempotência em produção:** substituir `IMemoryCache` por Redis.
5. **Rotação de credenciais:** mover `ClientSecret` para Azure Key Vault ou variável de ambiente.
