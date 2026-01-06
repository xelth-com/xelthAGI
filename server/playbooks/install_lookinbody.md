# Playbook: Install LookinBody 120

## Goal
Установить ПО LookinBody 120, пройти установку драйверов, активировать Hardlock Key и выполнить вход в систему.

## Variables
- InstallerPath: "C:\\Users\\Dmytro\\Desktop\\LookinBody120_Setup_MDR_V5.2.0.1_250417.exe"
- DefaultID: "inbody"
- DefaultPass: "0000"

## Steps

### 1. Запуск инсталлятора с повышенными правами
- **Action**: Запустить PowerShell скрипт для запуска установщика:
  ```
  os_run: powershell.exe
  args: -ExecutionPolicy Bypass -File "C:\\Users\\Dmytro\\xelthagi\\client\\SupportAgent\\Scripts\\launch_installer.ps1" -ExplicitPath "{InstallerPath}"
  ```
- **Note**: Скрипт автоматически:
  - Устанавливает правильную рабочую директорию (директория установщика)
  - Запускает с правами администратора (`-Verb RunAs`)
  - Вызывает UAC диалог (User Account Control)

### 1.1. Подтверждение UAC
- **Action**: `ask_user` с текстом: "A UAC (User Account Control) dialog should have appeared. Please click 'Yes' to allow the installer to run with administrator privileges. Click OK here when done."
- **Wait**: Дождаться появления окна "InstallShield Wizard" или окна с подтверждением переустановки.
- **Fallback**: Если окно не появляется после подтверждения UAC, использовать `inspect_screen` для проверки визуального состояния рабочего стола.

### 2. Подготовка и Приветствие
- **Check**: Если появится окно с предложением подключить USB сейчас, нажать "OK".
- **Find**: Окно "Welcome to the InstallShield Wizard".
- **Action**: Нажать "Next".

### 3. Лицензионное соглашение
- **Find**: Окно "License Agreement".
- **Action**: Выбрать радио-кнопку "I accept the terms of the license agreement".
- **Action**: Нажать "Next".
- **Wait**: Ждать окончания процесса установки (Progress bar).

### 4. Установка драйверов FTDI
- **Note**: Поверх основного окна появится установщик драйверов.
- **Find**: Окно "FTDI CDM Drivers".
- **Action**: Нажать "Extract".
- **Wait**: Появится "Device Driver Installation Wizard".
- **Action**: Нажать "Next".
- **Action**: Выбрать "I accept this agreement" и нажать "Next".
- **Action**: Нажать "Finish" когда появится сообщение "The drivers were successfully installed".

### 5. Установка драйверов CP210x
- **Note**: Следом появится второй установщик драйверов.
- **Find**: Окно "CP210x USB to UART Bridge Driver Installer".
- **Action**: Нажать "Next".
- **Action**: Выбрать "I accept this agreement" и нажать "Next".
- **Action**: Нажать "Finish".

### 6. Завершение InstallShield
- **Find**: Окно "InstallShield Wizard Complete".
- **Action**: Нажать "Finish".

### 7. Активация (Hardlock Key)
- **Note**: При первом запуске потребуется физический ключ.
- **Action**: `ask_user` с текстом: "Please insert the LookinBody Hardlock Key (USB) into the computer now. Click 'Yes' or 'OK' in this dialog when you have done so."
- **Verify**: Пользователь подтвердил действие.
- **Find**: Окно "LookinBody Activation" (если открыто).
- **Action**: Нажать "OK".
- **Verify**: Появится сообщение "LookinBody activated". Нажать "OK".

### 8. Первичная настройка (Welcome)
- **Find**: Окно "Welcome" (Select country/language).
- **Action**: Выбрать "USA" (или нужный регион) и "English".
- **Action**: Нажать "Save".
- **Action**: В окне выбора модели (Select the InBody model) оставить значение по умолчанию и нажать "Next".
- **Action**: В окне выбора соединения (Select the connection method) выбрать "USB" (или пропустить, нажав Next/Skip, если возможно).
- **Action**: Нажимать "Next" до появления кнопки "Done", затем нажать "Done".

### 9. Вход в систему
- **Find**: Окно входа "LookinBody 120".
- **Action**: Ввести ID: `{DefaultID}`
- **Action**: Ввести Password: `{DefaultPass}`
- **Action**: Нажать "Log in".
