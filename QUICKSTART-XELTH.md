# Быстрые команды для xelthAGI на xelth.com

## Основная информация

- **URL:** https://xelth.com/agi/
- **Путь:** /var/www/xelthAGI
- **Процесс:** xelthAGI (PM2)
- **Порт:** 3232
- **Модель:** gemini-3-flash-preview → gemini-2.5-flash (fallback)

## Проверка работы

```bash
# Health check
curl https://xelth.com/agi/health

# Статус PM2
pm2 status xelthAGI

# Логи
pm2 logs xelthAGI --lines 50
```

## Управление

```bash
# Перезапуск
pm2 restart xelthAGI

# Остановка
pm2 stop xelthAGI

# Запуск
pm2 start xelthAGI

# Мониторинг
pm2 monit
```

## Обновление

```bash
# 1. Обновить код
cd /var/www/xelthAGI
git pull
cd server
npm install

# 2. Изменить конфиг (если нужно)
nano /var/www/xelthAGI/server/.env

# 3. Перезапустить
pm2 restart xelthAGI

# 4. Проверить
pm2 logs xelthAGI --lines 20
curl https://xelth.com/agi/health
```

## Изменение API ключа

```bash
# Редактировать .env
nano /var/www/xelthAGI/server/.env

# Изменить GEMINI_API_KEY или переключиться на Claude
# LLM_PROVIDER=claude
# CLAUDE_API_KEY=your_key

# Перезапустить
pm2 restart xelthAGI
```

## Изменение модели

```bash
# Редактировать .env
nano /var/www/xelthAGI/server/.env

# Изменить GEMINI_MODEL
# Доступные: gemini-3-flash-preview, gemini-2.5-flash

# Перезапустить
pm2 restart xelthAGI
```

## Troubleshooting

```bash
# Логи с ошибками
pm2 logs xelthAGI --err

# Порт занят?
lsof -i :3232

# Nginx логи
tail -f /var/log/nginx/xelth.com.error.log

# Запустить вручную для отладки
cd /var/www/xelthAGI/server
node src/index.js
```

## Файлы конфигурации

- **Основной конфиг:** /var/www/xelthAGI/server/.env
- **PM2 конфиг:** /var/www/ecosystem.config.js
- **Nginx конфиг:** /etc/nginx/sites-available/xelth.com.conf

## Полная документация

→ [DEPLOYMENT-XELTH.md](DEPLOYMENT-XELTH.md)
