import json
from typing import Dict, Any, List
from anthropic import Anthropic
import google.generativeai as genai
from config import (
    LLM_PROVIDER,
    CLAUDE_API_KEY,
    GEMINI_API_KEY,
    CLAUDE_MODEL,
    GEMINI_MODEL,
    TEMPERATURE
)


class LLMService:
    def __init__(self):
        self.provider = LLM_PROVIDER.lower()

        if self.provider == "claude":
            self.client = Anthropic(api_key=CLAUDE_API_KEY)
            self.model = CLAUDE_MODEL
            print(f"✅ Using Claude: {self.model}")
        elif self.provider == "gemini":
            genai.configure(api_key=GEMINI_API_KEY)
            self.model = genai.GenerativeModel(GEMINI_MODEL)
            print(f"✅ Using Gemini: {GEMINI_MODEL}")
        else:
            raise ValueError(f"Unknown LLM provider: {self.provider}")

    def decide_next_action(self, ui_state: Dict[str, Any], task: str, history: List[str]) -> Dict[str, Any]:
        """
        Анализирует состояние UI и определяет следующее действие
        """
        prompt = self._build_prompt(ui_state, task, history)

        if self.provider == "claude":
            return self._ask_claude(prompt)
        elif self.provider == "gemini":
            return self._ask_gemini(prompt)

    def _build_prompt(self, ui_state: Dict[str, Any], task: str, history: List[str]) -> str:
        """
        Формирует промпт для LLM
        """
        elements_summary = self._summarize_elements(ui_state.get("Elements", []))
        history_text = "\n".join([f"  {i+1}. {h}" for i, h in enumerate(history[-10:])]) if history else "  (none)"

        prompt = f"""You are a UI automation agent. Your task is to help complete the following objective:

**TASK**: {task}

**CURRENT WINDOW**: {ui_state.get("WindowTitle", "Unknown")}

**ACTION HISTORY** (last 10 actions):
{history_text}

**AVAILABLE UI ELEMENTS**:
{elements_summary}

**YOUR JOB**:
Analyze the current UI state and determine the NEXT SINGLE ACTION to complete the task.

**RESPONSE FORMAT** (JSON only):
{{
    "action": "click|type|select|wait",
    "element_id": "element_automation_id",
    "text": "text to type (if action is 'type')",
    "message": "explanation of what you're doing",
    "task_completed": true|false,
    "reasoning": "why you chose this action"
}}

**RULES**:
1. Return ONLY ONE action at a time
2. If task is complete, set task_completed=true and action=""
3. Be precise - use exact element IDs from the list above
4. If element not found, try alternative approach or set task_completed=false with error message
5. Think step by step

Respond with JSON only, no additional text.
"""
        return prompt

    def _summarize_elements(self, elements: List[Dict[str, Any]]) -> str:
        """
        Создает краткое описание UI элементов
        """
        if not elements:
            return "  No UI elements found"

        summary_lines = []
        for elem in elements[:30]:  # Ограничиваем до 30 элементов
            name = elem.get("Name", "")
            elem_id = elem.get("Id", "")
            elem_type = elem.get("Type", "")
            value = elem.get("Value", "")
            enabled = elem.get("IsEnabled", False)

            status = "✓" if enabled else "✗"
            value_text = f" = '{value}'" if value else ""

            summary_lines.append(
                f"  [{status}] {elem_type}: '{name}' (id: {elem_id}){value_text}"
            )

        if len(elements) > 30:
            summary_lines.append(f"  ... and {len(elements) - 30} more elements")

        return "\n".join(summary_lines)

    def _ask_claude(self, prompt: str) -> Dict[str, Any]:
        """
        Запрашивает решение у Claude
        """
        try:
            response = self.client.messages.create(
                model=self.model,
                max_tokens=1024,
                temperature=TEMPERATURE,
                messages=[
                    {"role": "user", "content": prompt}
                ]
            )

            response_text = response.content[0].text.strip()

            # Извлекаем JSON из ответа
            return self._parse_json_response(response_text)

        except Exception as e:
            print(f"❌ Claude API error: {e}")
            return {
                "action": "",
                "message": f"Error: {str(e)}",
                "task_completed": False,
                "error": str(e)
            }

    def _ask_gemini(self, prompt: str) -> Dict[str, Any]:
        """
        Запрашивает решение у Gemini
        """
        try:
            response = self.model.generate_content(
                prompt,
                generation_config={
                    "temperature": TEMPERATURE,
                    "max_output_tokens": 1024,
                }
            )

            response_text = response.text.strip()

            # Извлекаем JSON из ответа
            return self._parse_json_response(response_text)

        except Exception as e:
            print(f"❌ Gemini API error: {e}")
            return {
                "action": "",
                "message": f"Error: {str(e)}",
                "task_completed": False,
                "error": str(e)
            }

    def _parse_json_response(self, response_text: str) -> Dict[str, Any]:
        """
        Парсит JSON ответ от LLM (с fallback на поиск JSON в тексте)
        """
        try:
            # Пытаемся распарсить как чистый JSON
            return json.loads(response_text)
        except json.JSONDecodeError:
            # Ищем JSON в markdown блоке
            if "```json" in response_text:
                start = response_text.find("```json") + 7
                end = response_text.find("```", start)
                json_text = response_text[start:end].strip()
                return json.loads(json_text)
            elif "```" in response_text:
                start = response_text.find("```") + 3
                end = response_text.find("```", start)
                json_text = response_text[start:end].strip()
                return json.loads(json_text)
            else:
                # Пытаемся найти JSON объект в тексте
                start = response_text.find("{")
                end = response_text.rfind("}") + 1
                if start >= 0 and end > start:
                    json_text = response_text[start:end]
                    return json.loads(json_text)

            raise ValueError(f"Could not parse JSON from response: {response_text[:200]}")
