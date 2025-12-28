from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any, Optional
from llm_service import LLMService
import config

app = FastAPI(title="Support Agent Server")
llm_service = LLMService()


class UIElement(BaseModel):
    Id: str
    Name: str
    Type: str
    Value: str
    IsEnabled: bool
    Bounds: Dict[str, int]


class UIState(BaseModel):
    WindowTitle: str
    ProcessName: str
    Elements: List[UIElement]
    Screenshot: str = ""


class ServerRequest(BaseModel):
    State: UIState
    Task: str
    History: List[str]


class Command(BaseModel):
    Action: str = ""
    ElementId: str = ""
    Text: str = ""
    X: int = 0
    Y: int = 0
    DelayMs: int = 100
    Message: str = ""


class ServerResponse(BaseModel):
    Command: Optional[Command] = None
    Success: bool
    Error: str = ""
    TaskCompleted: bool


@app.get("/health")
async def health_check():
    """Health check endpoint"""
    return {
        "status": "healthy",
        "llm_provider": config.LLM_PROVIDER,
        "model": config.CLAUDE_MODEL if config.LLM_PROVIDER == "claude" else config.GEMINI_MODEL
    }


@app.post("/decide", response_model=ServerResponse)
async def decide_next_action(request: ServerRequest):
    """
    Принимает состояние UI и возвращает следующую команду
    """
    try:
        # Конвертируем UIState в dict для LLM
        ui_state_dict = {
            "WindowTitle": request.State.WindowTitle,
            "ProcessName": request.State.ProcessName,
            "Elements": [elem.model_dump() for elem in request.State.Elements]
        }

        # Получаем решение от LLM
        decision = llm_service.decide_next_action(
            ui_state=ui_state_dict,
            task=request.Task,
            history=request.History
        )

        # Проверяем на ошибки от LLM
        if "error" in decision:
            return ServerResponse(
                Success=False,
                Error=decision["error"],
                TaskCompleted=False
            )

        # Проверяем завершение задачи
        task_completed = decision.get("task_completed", False)

        if task_completed:
            return ServerResponse(
                Command=Command(
                    Action="",
                    Message=decision.get("message", "Task completed successfully")
                ),
                Success=True,
                TaskCompleted=True
            )

        # Формируем команду
        command = Command(
            Action=decision.get("action", ""),
            ElementId=decision.get("element_id", ""),
            Text=decision.get("text", ""),
            Message=decision.get("message", ""),
            DelayMs=decision.get("delay_ms", 100)
        )

        print(f"🤖 Decision: {decision.get('reasoning', '')}")
        print(f"📤 Command: {command.Action} on {command.ElementId}")

        return ServerResponse(
            Command=command,
            Success=True,
            TaskCompleted=False
        )

    except Exception as e:
        print(f"❌ Error in /decide: {e}")
        raise HTTPException(status_code=500, detail=str(e))


if __name__ == "__main__":
    import uvicorn

    print("\n" + "="*50)
    print("🚀 Support Agent Server Starting...")
    print("="*50)
    print(f"LLM Provider: {config.LLM_PROVIDER}")
    print(f"Model: {config.CLAUDE_MODEL if config.LLM_PROVIDER == 'claude' else config.GEMINI_MODEL}")
    print(f"Server: http://{config.HOST}:{config.PORT}")
    print("="*50 + "\n")

    uvicorn.run(
        app,
        host=config.HOST,
        port=config.PORT,
        log_level="info" if config.DEBUG else "warning"
    )
