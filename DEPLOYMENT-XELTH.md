# XelthAGI Deployment on xelth.com Server

Специфичная инструкция для развёртывания на сервере xelth.com.

## Текущее развёртывание

- **Путь:** `/var/www/xelthAGI`
- **URL:** `https://xelth.com/agi/`
- **Порт:** `3232`
- **Процесс:** PM2 (xelthAGI)
- **Модель:** `gemini-3-flash-preview` (fallback: `gemini-2.5-flash`)

## Первоначальное развёртывание

### 1. Клонирование и установка

```bash
cd /var/www
git clone https://github.com/xelth-com/xelthAGI.git
cd xelthAGI/server
npm install
```

### 2. Настройка .env файла

```bash
cp .env.example .env
nano .env
```

**Важно:** Используйте следующие настройки:

```bash
# LLM Configuration
LLM_PROVIDER=gemini

# API Keys
GEMINI_API_KEY=your_gemini_api_key_here
CLAUDE_API_KEY=

# Model Selection
CLAUDE_MODEL=claude-sonnet-4-5-20250929
GEMINI_MODEL=gemini-3-flash-preview

# Server Configuration
HOST=0.0.0.0
PORT=3232
DEBUG=false

# Agent Configuration
MAX_STEPS=50
TEMPERATURE=0.7
```

**Защита .env файла:**
```bash
chmod 600 /var/www/xelthAGI/server/.env
```

### 3. Добавление в PM2 ecosystem

Отредактируйте `/var/www/ecosystem.config.js`:

```javascript
{
  name: 'xelthAGI',
  script: 'src/index.js',
  cwd: '/var/www/xelthAGI/server',
  watch: false,
  env_production: {
    NODE_ENV: 'production'
  }
}
```

### 4. Настройка Nginx

Отредактируйте `/etc/nginx/sites-available/xelth.com.conf`:

```nginx
# xelthAGI - AI Automation API
location /agi/ {
    proxy_pass http://localhost:3232/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection 'upgrade';
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_cache_bypass $http_upgrade;

    # Increased timeouts for long-running AI requests
    proxy_connect_timeout 120s;
    proxy_send_timeout 120s;
    proxy_read_timeout 120s;
}
```

**Применить изменения:**
```bash
nginx -t
systemctl reload nginx
```

### 5. Запуск сервиса

```bash
cd /var/www
pm2 start ecosystem.config.js --only xelthAGI
pm2 save
```

### 6. Проверка работы

```bash
# Локальный тест
curl http://localhost:3232/health

# Тест через nginx
curl https://xelth.com/agi/health
```

**Ожидаемый ответ:**
```json
{
  "status": "healthy",
  "llm_provider": "gemini",
  "model": "gemini-3-flash-preview",
  "server": "Node.js Express"
}
```

## Обновление сервиса

### Обновить код

```bash
cd /var/www/xelthAGI
git pull
cd server
npm install
```

### Изменить конфигурацию

```bash
nano /var/www/xelthAGI/server/.env
```

### Перезапустить сервис

```bash
pm2 restart xelthAGI
```

### Проверить логи

```bash
pm2 logs xelthAGI --lines 50
```

## Управление сервисом

### PM2 команды

```bash
# Статус
pm2 status
pm2 status xelthAGI

# Логи
pm2 logs xelthAGI
pm2 logs xelthAGI --lines 100
pm2 logs xelthAGI --err       # только ошибки

# Управление
pm2 restart xelthAGI
pm2 stop xelthAGI
pm2 start xelthAGI
pm2 delete xelthAGI

# Мониторинг
pm2 monit
```

### Проверка здоровья

```bash
# Health endpoint
curl https://xelth.com/agi/health

# Детальная проверка
curl -v https://xelth.com/agi/health

# Локальный порт
curl http://localhost:3232/health
```

## Fallback система

Сервис автоматически переключается между моделями:

1. **Основная модель:** `gemini-3-flash-preview`
2. **Fallback модель:** `gemini-2.5-flash`

Если основная модель недоступна, сервис автоматически использует fallback.

Логика в файле: `/var/www/xelthAGI/server/src/llmService.js` (строки 113-160)

## Troubleshooting

### Сервис не стартует

```bash
# Проверить логи
pm2 logs xelthAGI --err

# Запустить вручную для отладки
cd /var/www/xelthAGI/server
node src/index.js
```

### Порт занят

```bash
# Найти процесс на порту 3232
lsof -i :3232
ss -tulpn | grep 3232

# Убить процесс
kill -9 <PID>
```

### API ключ не работает

```bash
# Проверить .env
cat /var/www/xelthAGI/server/.env

# Проверить права
ls -la /var/www/xelthAGI/server/.env
# Должно быть: -rw------- (600)
```

### Nginx не проксирует

```bash
# Проверить конфигурацию
nginx -t

# Проверить логи nginx
tail -f /var/log/nginx/xelth.com.error.log

# Перезагрузить nginx
systemctl reload nginx
```

### Модель недоступна

Проверить логи на ошибки API:
```bash
pm2 logs xelthAGI | grep -i "error\|fail"
```

Попробовать переключиться на Claude:
```bash
nano /var/www/xelthAGI/server/.env
# Изменить: LLM_PROVIDER=claude
# Добавить: CLAUDE_API_KEY=your_key

pm2 restart xelthAGI
```

## Endpoints

### Health Check (GET)
```bash
curl https://xelth.com/agi/health
```

### Decision Endpoint (POST)
```bash
curl -X POST https://xelth.com/agi/decide \
  -H "Content-Type: application/json" \
  -d '{
    "ClientId": "test-client",
    "Task": "Test task",
    "State": {
      "WindowTitle": "Test Window",
      "ProcessName": "test.exe",
      "Elements": []
    },
    "History": []
  }'
```

## Безопасность

1. **API ключ защищён:**
   - Файл `.env` имеет права `600` (только root)
   - `.env` в `.gitignore` - не попадёт в git

2. **HTTPS:**
   - Используется существующий SSL сертификат
   - Все запросы через HTTPS

3. **Firewall:**
   - Порт 3232 не открыт извне
   - Доступ только через Nginx reverse proxy

## Файловая структура

```
/var/www/
├── ecosystem.config.js          # PM2 конфигурация для всех сервисов
└── xelthAGI/
    ├── server/
    │   ├── .env                 # Конфигурация (НЕ в git!)
    │   ├── .env.example         # Пример конфигурации
    │   ├── package.json
    │   └── src/
    │       ├── index.js         # Главный файл
    │       ├── config.js        # Загрузка конфигурации
    │       ├── llmService.js    # Логика AI (с fallback)
    │       └── llm.provider.js  # Инициализация провайдеров
    └── DEPLOYMENT-XELTH.md      # Эта инструкция

/etc/nginx/sites-available/
└── xelth.com.conf               # Nginx конфигурация с /agi/ proxy
```

## Мониторинг

### Ресурсы процесса
```bash
pm2 monit
```

### Логи в реальном времени
```bash
pm2 logs xelthAGI --lines 0
```

### Статистика
```bash
pm2 show xelthAGI
```

## Контакты и поддержка

- **GitHub:** https://github.com/xelth-com/xelthAGI
- **Issues:** https://github.com/xelth-com/xelthAGI/issues

---

**Последнее обновление:** 2025-12-29
**Развёрнуто на:** xelth.com
**Версия:** 1.0.0
