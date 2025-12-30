# Задача для следующей инстанции Claude

## Текущая проблема: Self-Healing не работает должным образом

### Что уже сделано:
✅ Реализован State Tracking - клиент логирует изменения UI состояния
✅ Добавлены coordinate-based clicks - резервный метод кликов
✅ Добавлен промпт для LLM о self-healing логике
✅ История действий содержит маркеры "UI State unchanged - action may have failed!"

### Что НЕ работает:
❌ LLM видит маркеры "UI State unchanged" но продолжает повторять одинаковые действия (клики)
❌ 50 шагов из 50 - бесконечные клики по элементам
❌ Задача не выполнена - агент застрял в цикле

### Корневая причина:
**Неправильная детекция изменений состояния:**
- Сейчас проверяется: `WindowTitle` и `Elements.Count`
- Проблема: При кликах в Notepad эти параметры НЕ меняются
- Результат: Система считает что клик не сработал, хотя фокус установлен
- LLM получает "unchanged" и пытается снова, но промпт недостаточно строгий

### Файл для изучения проблемы:
Последний тестовый лог: `C:\Users\xelth\AppData\Local\Temp\claude\C--Users-xelth-xelthAGI\tasks\b9fb325.output`
- 50 шагов, все с маркером "UI State unchanged"
- Агент кликает по элементам, но состояние не меняется (по метрике Title+Count)

---

## Задачи для реализации:

### 1. Улучшить детекцию изменений состояния (КРИТИЧНО)

**Файл:** `client/SupportAgent/Program.cs`

**Проблема:** Сейчас сравнивается только Title и Elements.Count
```csharp
// ПЛОХО - не показывает реальные изменения
var stateChanged = (newTitle != previousTitle) || (newCount != previousElementCount);
```

**Решение:** Добавить проверку Value текстовых элементов
```csharp
// Перед командой:
var textElements = uiState.Elements.Where(e => e.Type.Contains("Text") || e.Type.Contains("Edit"));
var previousValues = textElements.Select(e => e.Value).ToList();
var previousValuesHash = string.Join("|", previousValues);

// После команды:
var newValues = newState.Elements.Where(e => e.Type.Contains("Text") || e.Type.Contains("Edit"));
var newValuesHash = string.Join("|", newValues.Select(e => e.Value));

var contentChanged = previousValuesHash != newValuesHash;
```

**Дополнительно:**
- Добавить в историю информацию об изменении контента: `[Content: "old" -> "new"]`
- Это позволит LLM видеть что текст изменился даже если Title не поменялся

### 2. Ужесточить промпт Self-Healing (КРИТИЧНО)

**Файл:** `server/src/llmService.js`

**Текущая проблема:** Промпт слишком мягкий, LLM его игнорирует

**Решение:** Добавить ЖЕСТКИЕ правила с примерами:

```markdown
**CRITICAL SELF-HEALING RULE:**
1. Look at last 3 actions in history
2. If you see "UI State unchanged" 3+ times in a row for SAME action type (e.g., all "click"):
   → STOP clicking immediately
   → This means clicks are NOT working
   → Switch to alternative: use keyboard commands, request screenshot, or try different approach

**FORBIDDEN:**
- Repeating same action >3 times when seeing "unchanged"
- Clicking different element IDs when all show "unchanged" (Notepad generates new IDs)

**EXAMPLE:**
History shows:
- click element A [unchanged]
- click element B [unchanged]
- click element C [unchanged]

→ STOP clicking! Elements are not the problem.
→ Try: Ctrl+A + Delete to clear, or request screenshot
```

### 3. Добавить счетчик повторяющихся действий

**Файл:** `server/src/llmService.js` метод `_buildPrompt`

**Логика:**
```javascript
// Анализировать историю
const lastActions = history.slice(-5).map(h => {
    const actionMatch = h.match(/^(\w+)\s/);
    return actionMatch ? actionMatch[1] : '';
});

const sameActionCount = lastActions.filter(a => a === lastActions[lastActions.length-1]).length;

if (sameActionCount >= 3) {
    prompt += `\n\n**WARNING: Same action repeated ${sameActionCount} times! CHANGE STRATEGY NOW!**`;
}
```

### 4. Опциональное улучшение: Timeout на одинаковые действия

**Файл:** `client/SupportAgent/Program.cs`

**Идея:** На клиентской стороне отслеживать последние N действий. Если одно и то же действие 5+ раз подряд - ПРЕРВАТЬ выполнение с ошибкой.

---

## Доступ к серверу

### SSH подключение

**Файл с данными доступа:** `../.claude/NOTES.md`

**Сервер:**
- Hostname: 152.53.15.15 (antigravity)
- User: root
- SSH Alias: `antigravity`
- SSH Key: ~/.ssh/netcup
- Команда: `ssh antigravity`

**Путь к проекту на сервере:**
- `/var/www/xelthAGI/`
- Сервер: Node.js Express на порту 3232
- PM2 процесс: `xelthAGI`
- URL: https://xelth.com/AGI

### Процесс деплоя

1. **Сборка клиента:**
```bash
cd /c/Users/xelth/xelthAGI/client/SupportAgent
dotnet build -c Release
```

2. **Коммит изменений:**
```bash
cd /c/Users/xelth/xelthAGI
git add -A
git commit -m "fix: improve self-healing state detection"
git push
```

3. **Деплой на сервер:**
```bash
ssh antigravity "cd /var/www/xelthAGI && git pull && pm2 restart xelthAGI"
```

4. **Проверка:**
```bash
# Health check
curl https://xelth.com/AGI/HEALTH

# Логи
ssh antigravity "pm2 logs xelthAGI --lines 50"
```

---

## Файлы для изменения

### Клиент (C#):
1. `client/SupportAgent/Program.cs`
   - Улучшить детекцию изменений состояния (добавить Value tracking)
   - Добавить в историю информацию об изменении контента

### Сервер (Node.js):
1. `server/src/llmService.js`
   - Ужесточить промпт self-healing (добавить FORBIDDEN rules)
   - Добавить счетчик повторяющихся действий
   - Добавить WARNING в промпт при обнаружении цикла

---

## Тестирование

После изменений запустить тест:
```bash
cd /c/Users/xelth/xelthAGI/client/SupportAgent/bin/Release/net8.0-windows/win-x64
./SupportAgent.exe --app notepad --task "Clear all text and write: Self-healing v2 works!" --server https://xelth.com/AGI
```

**Ожидаемый результат:**
- ✅ Агент должен увидеть что клики не меняют контент
- ✅ После 2-3 неудачных попыток сменить стратегию (Ctrl+A + Delete)
- ✅ Записать текст
- ✅ Завершить задачу в <20 шагов

**Критерии успеха:**
- Нет бесконечных циклов одинаковых действий
- История показывает изменения контента: `[Content: "old" -> "new"]`
- Задача выполнена успешно

---

## Дополнительные заметки

**История сессии:**
- Реализованы: Playbooks, On-Demand Vision, Window Focus Verification, Slow Character Typing
- Последний успешный тест: 12 шагов, текст введен полностью
- Текущая проблема: Self-healing детектирует но не реагирует на "unchanged"

**Git commits:**
- `14a13b6` - slow character typing
- `f755e5d` - content verification and keyboard commands
- `9d41480` - window focus verification
- `4d1613d` - playbooks + on-demand vision + file downloads

**Агент который работал над self-healing:**
- ID: `a750469`
- Статус: Реализовал coordinate clicks и state tracking, но проблема с циклами осталась

---

## Команды для быстрого старта

```bash
# 1. Прочитать текущие файлы
cat client/SupportAgent/Program.cs | grep -A 20 "previousTitle"
cat server/src/llmService.js | grep -A 30 "SELF-HEALING"

# 2. Посмотреть последний тестовый лог
tail -100 /c/Users/xelth/AppData/Local/Temp/claude/C--Users-xelth-xelthAGI/tasks/b9fb325.output

# 3. Проверить статус сервера
ssh antigravity "pm2 status xelthAGI && git -C /var/www/xelthAGI log -1 --oneline"
```

---

Удачи! 🚀
