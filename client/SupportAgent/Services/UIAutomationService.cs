using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using SupportAgent.Models;
using System.Drawing;
using System.Drawing.Imaging;

namespace SupportAgent.Services;

public class UIAutomationService : IDisposable
{
    private readonly UIA3Automation _automation;
    private Window? _currentWindow;
    private Dictionary<string, AutomationElement> _elementCache = new();

    public UIAutomationService()
    {
        _automation = new UIA3Automation();
    }

    /// <summary>
    /// Находит окно по имени процесса или заголовку
    /// </summary>
    public Window? FindWindow(string processNameOrTitle)
    {
        var desktop = _automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        Window? fallbackMatch = null;

        foreach (var window in windows)
        {
            var title = window.Name ?? "";

            try
            {
                // Получаем имя процесса
                var processId = window.Properties.ProcessId.ValueOrDefault;
                var process = System.Diagnostics.Process.GetProcessById(processId);
                var processName = process.ProcessName;

                // 1. Точное совпадение по имени процесса (наивысший приоритет)
                if (processName.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    _currentWindow = window.AsWindow();
                    return _currentWindow;
                }

                // 2. Точное совпадение или начало заголовка
                if (title.Equals(processNameOrTitle, StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    _currentWindow = window.AsWindow();
                    return _currentWindow;
                }

                // 3. Contains как fallback (низкий приоритет)
                if (fallbackMatch == null && title.Contains(processNameOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    // Исключаем окна командной строки и терминалов
                    if (!processName.Equals("cmd", StringComparison.OrdinalIgnoreCase) &&
                        !processName.Equals("powershell", StringComparison.OrdinalIgnoreCase) &&
                        !processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackMatch = window.AsWindow();
                    }
                }
            }
            catch
            {
                // Процесс может быть недоступен
            }
        }

        _currentWindow = fallbackMatch;
        return _currentWindow;
    }

    /// <summary>
    /// Сканирует текущее состояние окна
    /// </summary>
    public UIState GetWindowState(Window window)
    {
        // Очищаем кеш перед новым сканированием
        _elementCache.Clear();

        var state = new UIState
        {
            WindowTitle = window.Name ?? "",
            ProcessName = window.Properties.ProcessId.ValueOrDefault.ToString(),
            Elements = new List<UIElement>()
        };

        // Рекурсивно собираем все элементы
        ScanElements(window, state.Elements, maxDepth: 10);

        return state;
    }

    private void ScanElements(AutomationElement element, List<UIElement> elements, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth) return;

        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                // Генерируем стабильный ID
                var elementId = child.Properties.AutomationId.ValueOrDefault ?? Guid.NewGuid().ToString();

                var uiElement = new UIElement
                {
                    Id = elementId,
                    Name = child.Name ?? "",
                    Type = child.ControlType.ToString(),
                    Value = GetElementValue(child),
                    IsEnabled = child.IsEnabled,
                    Bounds = new Models.Rectangle
                    {
                        X = (int)child.BoundingRectangle.X,
                        Y = (int)child.BoundingRectangle.Y,
                        Width = (int)child.BoundingRectangle.Width,
                        Height = (int)child.BoundingRectangle.Height
                    }
                };

                // Сохраняем элемент в кеш для быстрого доступа
                _elementCache[elementId] = child;

                // Добавляем только значимые элементы
                if (IsSignificantElement(uiElement))
                {
                    elements.Add(uiElement);
                }

                // Рекурсия для вложенных элементов
                ScanElements(child, elements, maxDepth, currentDepth + 1);
            }
        }
        catch
        {
            // Некоторые элементы могут быть недоступны
        }
    }

    private string GetElementValue(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                return element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
            }
            if (element.Patterns.Text.IsSupported)
            {
                return element.Patterns.Text.Pattern.DocumentRange.GetText(-1) ?? "";
            }
        }
        catch { }

        return "";
    }

    private bool IsSignificantElement(UIElement element)
    {
        // Фильтруем незначимые элементы
        if (string.IsNullOrWhiteSpace(element.Name) && string.IsNullOrWhiteSpace(element.Value))
            return false;

        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Выполняет команду на элементе
    /// </summary>
    public async Task<bool> ExecuteCommand(Window window, Command command)
    {
        try
        {
            await Task.Delay(command.DelayMs);

            switch (command.Action.ToLower())
            {
                case "click":
                    return ClickElement(window, command.ElementId);

                case "type":
                    return TypeText(window, command.ElementId, command.Text);

                case "select":
                    return SelectItem(window, command.ElementId, command.Text);

                case "mouse_move":
                    MoveMouse(command.X, command.Y);
                    return true;

                case "wait":
                    await Task.Delay(command.DelayMs);
                    return true;

                default:
                    Console.WriteLine($"Unknown command: {command.Action}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing command: {ex.Message}");
            return false;
        }
    }

    private bool ClickElement(Window window, string elementId)
    {
        var element = FindElementById(window, elementId);
        if (element == null)
        {
            Console.WriteLine($"Element not found: {elementId}");
            return false;
        }

        element.Click();
        return true;
    }

    private bool TypeText(Window window, string elementId, string text)
    {
        var element = FindElementById(window, elementId);
        if (element == null) return false;

        element.Focus();
        Keyboard.Type(text);
        return true;
    }

    private bool SelectItem(Window window, string elementId, string itemText)
    {
        var element = FindElementById(window, elementId);
        if (element == null) return false;

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return true;
        }

        return false;
    }

    private void MoveMouse(int x, int y)
    {
        FlaUI.Core.Input.Mouse.MoveTo(new System.Drawing.Point(x, y));
    }

    private AutomationElement? FindElementById(Window window, string id)
    {
        // Сначала проверяем кеш
        if (_elementCache.TryGetValue(id, out var cachedElement))
        {
            return cachedElement;
        }

        // Если не в кеше, ищем по AutomationId
        try
        {
            return window.FindFirstDescendant(cf => cf.ByAutomationId(id));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _automation?.Dispose();
    }
}
