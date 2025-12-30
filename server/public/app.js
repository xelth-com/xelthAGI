// Mission Control - Real-time Dashboard
// Polls /api/state every 2 seconds for updates

const POLL_INTERVAL = 2000; // 2 seconds
let previousHistoryLength = 0;

// DOM Elements
const statusDot = document.getElementById('statusDot');
const statusText = document.getElementById('statusText');
const taskText = document.getElementById('taskText');
const screenshotViewer = document.getElementById('screenshotViewer');
const agentThoughts = document.getElementById('agentThoughts');
const statActions = document.getElementById('statActions');
const statWindow = document.getElementById('statWindow');
const statLastSeen = document.getElementById('statLastSeen');
const historyList = document.getElementById('historyList');

// Fetch and update dashboard state
async function updateDashboard() {
    try {
        const response = await fetch('api/state');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const state = await response.json();

        // Update status indicator
        updateStatus(state.isOnline);

        // Update task
        updateTask(state.task);

        // Update screenshot
        updateScreenshot(state.screenshot);

        // Update agent thoughts
        updateThoughts(state.lastDecision);

        // Update statistics
        updateStats(state);

        // Update history
        updateHistory(state.history);

    } catch (error) {
        console.error('Failed to fetch state:', error);
        updateStatus(false);
    }
}

// Update status indicator
function updateStatus(isOnline) {
    if (isOnline) {
        statusDot.classList.remove('offline');
        statusDot.classList.add('online');
        statusText.textContent = 'Online';
    } else {
        statusDot.classList.remove('online');
        statusDot.classList.add('offline');
        statusText.textContent = 'Offline';
    }
}

// Update current task
function updateTask(task) {
    if (task && task.trim()) {
        taskText.textContent = task;
    } else {
        taskText.textContent = 'No active task';
    }
}

// Update screenshot viewer
function updateScreenshot(screenshotBase64) {
    if (screenshotBase64 && screenshotBase64.trim()) {
        screenshotViewer.classList.remove('empty');
        screenshotViewer.innerHTML = `<img src="data:image/jpeg;base64,${screenshotBase64}" alt="Agent Screenshot">`;
    } else {
        screenshotViewer.classList.add('empty');
        screenshotViewer.innerHTML = '<span>No screenshot available</span>';
    }
}

// Update agent thoughts
function updateThoughts(lastDecision) {
    if (lastDecision && lastDecision.reasoning) {
        agentThoughts.textContent = lastDecision.reasoning;
    } else if (lastDecision && lastDecision.message) {
        agentThoughts.textContent = lastDecision.message;
    } else {
        agentThoughts.textContent = 'Waiting for agent activity...';
    }
}

// Update statistics
function updateStats(state) {
    // Total actions
    statActions.textContent = state.totalActions || 0;

    // Current window
    if (state.uiState && state.uiState.WindowTitle) {
        const windowTitle = state.uiState.WindowTitle;
        // Truncate long titles
        statWindow.textContent = windowTitle.length > 15
            ? windowTitle.substring(0, 15) + '...'
            : windowTitle;
    } else {
        statWindow.textContent = '—';
    }

    // Last seen (time ago)
    if (state.lastSeen) {
        const lastSeenDate = new Date(state.lastSeen);
        const now = new Date();
        const diffSeconds = Math.floor((now - lastSeenDate) / 1000);

        if (diffSeconds < 5) {
            statLastSeen.textContent = 'Now';
        } else if (diffSeconds < 60) {
            statLastSeen.textContent = `${diffSeconds}s`;
        } else if (diffSeconds < 3600) {
            statLastSeen.textContent = `${Math.floor(diffSeconds / 60)}m`;
        } else {
            statLastSeen.textContent = `${Math.floor(diffSeconds / 3600)}h`;
        }
    } else {
        statLastSeen.textContent = '—';
    }
}

// Update action history
function updateHistory(history) {
    if (!history || history.length === 0) {
        historyList.innerHTML = '<div class="empty-state">No actions yet</div>';
        previousHistoryLength = 0;
        return;
    }

    // Only update if history changed
    if (history.length === previousHistoryLength) {
        return;
    }

    previousHistoryLength = history.length;

    // Clear and rebuild history list
    historyList.innerHTML = '';

    // Show last 20 items (reverse order - newest first)
    const recentHistory = history.slice(-20).reverse();

    recentHistory.forEach((item, index) => {
        const historyItem = document.createElement('div');
        historyItem.className = 'history-item';

        // Determine item type for styling
        if (item.includes('ERROR') || item.includes('FAILED')) {
            historyItem.classList.add('error');
        } else if (item.includes('SUCCESS') || item.includes('✅')) {
            historyItem.classList.add('success');
        } else {
            historyItem.classList.add('info');
        }

        // Create timestamp (actual index from end)
        const actualIndex = history.length - index;
        const timestamp = document.createElement('div');
        timestamp.className = 'timestamp';
        timestamp.textContent = `#${actualIndex}`;

        // Create action text
        const action = document.createElement('div');
        action.className = 'action';

        // Truncate very long messages
        let displayText = item;
        if (displayText.length > 200) {
            displayText = displayText.substring(0, 200) + '...';
        }
        action.textContent = displayText;

        historyItem.appendChild(timestamp);
        historyItem.appendChild(action);
        historyList.appendChild(historyItem);
    });

    // Auto-scroll to top (newest items)
    historyList.scrollTop = 0;
}

// Format time ago
function timeAgo(dateString) {
    const date = new Date(dateString);
    const now = new Date();
    const seconds = Math.floor((now - date) / 1000);

    if (seconds < 10) return 'just now';
    if (seconds < 60) return `${seconds}s ago`;
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
    return `${Math.floor(seconds / 86400)}d ago`;
}

// Initial update
updateDashboard();

// Poll for updates
setInterval(updateDashboard, POLL_INTERVAL);

// Log to console
console.log('🚀 Mission Control dashboard initialized');
console.log(`📡 Polling /api/state every ${POLL_INTERVAL/1000} seconds`);
