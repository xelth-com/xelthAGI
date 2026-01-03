// Mission Control - Real-time Dashboard v2.0
// Polls API every 2 seconds

const POLL_INTERVAL = 2000;
let previousHistoryLength = 0;
let currentSessionName = '';
let currentStepCount = 0;

// DOM Elements
const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const taskText = document.getElementById('taskText');
const agentViewer = document.getElementById('agentViewer');
const shadowViewer = document.getElementById('shadowViewer');
const playbackInfo = document.getElementById('playbackInfo');
const agentThoughts = document.getElementById('agentThoughts');
const statActions = document.getElementById('statActions');
const statWindow = document.getElementById('statWindow');
const statLastSeen = document.getElementById('statLastSeen');
const historyList = document.getElementById('historyList');
const logsList = document.getElementById('logsList');
const debugToggle = document.getElementById('debugToggle');

// Toggle Debug Mode (UPPERCASE API)
debugToggle.addEventListener('change', async (e) => {
    try {
        const response = await fetch('API/SETTINGS', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ debug: e.target.checked })
        });
        const data = await response.json();
        console.log('Debug mode set to:', data.debug);
    } catch (err) {
        console.error('Failed to toggle debug:', err);
        // Revert toggle if failed
        e.target.checked = !e.target.checked;
    }
});

// Fetch and update dashboard state (UPPERCASE API)
async function updateDashboard() {
    try {
        const response = await fetch('API/STATE');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const state = await response.json();

        // Update basic info
        updateStatus(state.isOnline);
        updateTask(state.task);
        updateStats(state);
        updateThoughts(state.lastDecision);

        // Sync debug toggle with server state
        if (state.serverDebugMode !== undefined && document.activeElement !== debugToggle) {
            debugToggle.checked = state.serverDebugMode;
        }

        // Store session info for image construction
        if (state.sessionName) {
            currentSessionName = state.sessionName;
        }

        // Update Images
        updateAgentVision(state.screenshot);

        // Update History (and triggers shadow image update for latest)
        updateHistory(state.history);

    } catch (error) {
        console.error('Failed to fetch state:', error);
        updateStatus(false);
    }
}

// Update status
function updateStatus(isOnline) {
    if (isOnline) {
        statusDot.className = 'status-dot online';
        statusText.textContent = 'Online';
    } else {
        statusDot.className = 'status-dot offline';
        statusText.textContent = 'Offline';
    }
}

// Update task
function updateTask(task) {
    taskText.textContent = (task && task.trim()) ? task : 'No active task';
}

// Update Agent Vision (Cropped/Focused)
function updateAgentVision(screenshotBase64) {
    if (screenshotBase64 && screenshotBase64.trim()) {
        agentViewer.classList.remove('empty');
        agentViewer.innerHTML = `<img src="data:image/jpeg;base64,${screenshotBase64}" alt="Agent Vision">`;
    } else {
        agentViewer.classList.add('empty');
        agentViewer.innerHTML = '<span class="placeholder">Waiting for request...</span>';
    }
}

// Load Shadow Image for a specific step
function loadShadowImage(stepNumber) {
    if (!currentSessionName) return;

    // Construct URL: /AGI/SCREENSHOTS/{Session}/{step_00X}.jpg
    const paddedStep = String(stepNumber).padStart(3, '0');
    const imageUrl = `/AGI/SCREENSHOTS/${currentSessionName}/step_${paddedStep}.jpg`;

    // Create image with error handling
    const img = new Image();
    img.onload = () => {
        shadowViewer.classList.remove('empty');
        shadowViewer.innerHTML = '';
        shadowViewer.appendChild(img);
        playbackInfo.textContent = `Viewing Step #${stepNumber}`;
    };
    img.onerror = () => {
        // Only show error if we expected an image (i.e. not step 0)
        if (stepNumber > 0) {
            playbackInfo.textContent = `Step #${stepNumber}: No image available`;
        }
    };
    img.src = imageUrl;
}

// Update thoughts
function updateThoughts(lastDecision) {
    if (lastDecision && lastDecision.reasoning) {
        agentThoughts.textContent = lastDecision.reasoning;
    } else if (lastDecision && lastDecision.message) {
        agentThoughts.textContent = lastDecision.message;
    } else {
        agentThoughts.textContent = 'Waiting for agent activity...';
    }
}

// Update stats
function updateStats(state) {
    statActions.textContent = state.totalActions || 0;

    if (state.uiState && state.uiState.WindowTitle) {
        const title = state.uiState.WindowTitle;
        statWindow.textContent = title.length > 15 ? title.substring(0, 15) + '...' : title;
    } else {
        statWindow.textContent = '—';
    }

    if (state.lastSeen) {
        const diff = Math.floor((new Date() - new Date(state.lastSeen)) / 1000);
        statLastSeen.textContent = diff < 60 ? `${diff}s ago` : `${Math.floor(diff/60)}m ago`;
    }
}

// Update History List
function updateHistory(history) {
    if (!history || history.length === 0) {
        historyList.innerHTML = '<div class="empty-state">No actions yet</div>';
        previousHistoryLength = 0;
        return;
    }

    // Only update DOM if history changed
    if (history.length === previousHistoryLength) return;

    // If new steps added, update shadow view to latest
    if (history.length > previousHistoryLength) {
        loadShadowImage(history.length);
    }

    previousHistoryLength = history.length;
    currentStepCount = history.length;
    historyList.innerHTML = '';

    // Show reverse order
    const recentHistory = history.slice().reverse();

    recentHistory.forEach((item, index) => {
        const actualIndex = history.length - index; // Step number 1-based
        const historyItem = document.createElement('div');
        historyItem.className = 'history-item';

        if (item.includes('ERROR') || item.includes('FAILED')) historyItem.classList.add('error');
        else if (item.includes('SUCCESS') || item.includes('✅')) historyItem.classList.add('success');
        else historyItem.classList.add('info');

        // Click handler for Time Travel
        historyItem.onclick = () => {
            loadShadowImage(actualIndex);
            // Highlight active
            document.querySelectorAll('.history-item').forEach(el => el.style.borderRight = 'none');
            historyItem.style.borderRight = '4px solid var(--accent-primary)';
        };

        const timestamp = document.createElement('div');
        timestamp.className = 'timestamp';
        timestamp.textContent = `Step #${actualIndex}`;

        const action = document.createElement('div');
        action.className = 'action';
        action.textContent = item.length > 150 ? item.substring(0, 150) + '...' : item;

        historyItem.appendChild(timestamp);
        historyItem.appendChild(action);
        historyList.appendChild(historyItem);
    });
}

// Update Logs List (UPPERCASE API)
async function updateLogs() {
    try {
        const response = await fetch('API/LOGS');
        if (!response.ok) return;
        const logs = await response.json();

        const logsList = document.getElementById('logsList');
        if (logs.length === 0) {
            logsList.innerHTML = '<div class="empty-state">No logs found</div>';
            return;
        }

        logsList.innerHTML = logs.map(log => `
            <div class="history-item info" style="display: flex; justify-content: space-between; align-items: center;">
                <div style="overflow: hidden; white-space: nowrap; text-overflow: ellipsis; max-width: 60%;">
                    <div class="timestamp">${new Date(log.time).toLocaleTimeString()}</div>
                    <div class="action" style="font-size: 0.8rem;">${log.name}</div>
                </div>
                <div style="display: flex; gap: 0.5rem;">
                    ${log.screenshotsUrl ? `<a href="${log.screenshotsUrl}" target="_blank" class="badge debug" style="text-decoration: none;">📁 PICS</a>` : ''}
                    <a href="${log.url}" target="_blank" class="badge" style="text-decoration: none;">⬇️ JSON</a>
                </div>
            </div>
        `).join('');
    } catch (e) {
        console.error('Failed to fetch logs', e);
    }
}

// Init
updateDashboard();
updateLogs();
setInterval(updateDashboard, POLL_INTERVAL);
setInterval(updateLogs, 5000);
