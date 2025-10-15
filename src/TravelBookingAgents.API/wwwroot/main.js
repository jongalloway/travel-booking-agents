const chatWindow = document.getElementById('chat-window');
const chatForm = document.getElementById('chat-form');
const chatInput = document.getElementById('chat-input');

function formatTimestamp(ts) {
    return new Date(ts).toLocaleString();
}

function renderAllMarkdown(text) {
    // Remove leading/trailing quotes if present
    let normalizedText = text.trim();
    if (normalizedText.startsWith('"') && normalizedText.endsWith('"')) {
        normalizedText = normalizedText.slice(1, -1);
    }
    // Replace escaped newlines (\n) with actual newlines
    normalizedText = normalizedText.replace(/\\n/g, '\n');
    
    let result = '';
    
    // Extract markdown code blocks
    const codeBlocks = [];
    const regex = /```markdown\n([\s\S]*?)```/g;
    let match;
    
    while ((match = regex.exec(normalizedText)) !== null) {
        codeBlocks.push(match[1]);
    }
    
    // Remove code blocks from the original text to get the main content
    const mainContent = normalizedText.replace(/```markdown\n[\s\S]*?```/g, '').trim();
    
    // Render main content if it exists
    if (mainContent) {
        result += `<div class="markdown">${marked.parse(mainContent)}</div>`;
    }
    
    // Render each code block
    codeBlocks.forEach(block => {
        result += `<div class="markdown">${marked.parse(block)}</div>`;
    });
    
    return result;
}

function renderContent(content) {
    if (content.$type === "text") {
        return renderAllMarkdown(content.text);
    }
    if (content.$type === "functionCall") {
        return `<div class="func-call">
            <span class="func-name">${content.name}</span>
            <span class="func-args">${JSON.stringify(content.arguments)}</span>
        </div>`;
    }
    if (content.$type === "functionResult") {
        return `<div class="func-result">
            <span class="func-result-label">Result:</span>
            <span class="func-result-value">${content.result}</span>
        </div>`;
    }
    return `<div class="unknown">${JSON.stringify(content)}</div>`;
}

function appendStructuredMessage(msg) {
    const msgDiv = document.createElement('div');
    msgDiv.className = `message bot`;
    msgDiv.innerHTML = `
        <div class="author">${msg.authorName} <span class="timestamp">${formatTimestamp(msg.createdAt)}</span></div>
        ${msg.contents.map(renderContent).join('')}
    `;
    chatWindow.appendChild(msgDiv);
    chatWindow.scrollTop = chatWindow.scrollHeight;
}

function appendMessage(text, sender) {
    const msgDiv = document.createElement('div');
    msgDiv.className = `message ${sender}`;
    const bubble = document.createElement('div');
    bubble.className = `bubble ${sender}`;
    bubble.textContent = text;
    msgDiv.appendChild(bubble);
    chatWindow.appendChild(msgDiv);
    chatWindow.scrollTop = chatWindow.scrollHeight;
}

chatForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const userMsg = chatInput.value.trim();
    if (!userMsg) return;
    appendMessage(userMsg, 'user');
    chatInput.value = '';
    appendMessage('...', 'bot');
    try {
        const res = await fetch('/agent/chat?prompt=' + encodeURIComponent(userMsg));
        if (!res.ok) throw new Error('Network error');
        const data = await res.json();
        // Remove loading
        chatWindow.lastChild.remove();
        // Display structured messages if available
        if (data.messages) {
            // Extract only text content from all messages
            let textContent = '';
            data.messages.forEach(msg => {
                msg.contents.forEach(content => {
                    if (content.$type === 'text') {
                        textContent = content.text; // Only keep the last text content
                    }
                });
            });
            
            if (textContent.trim()) {
                const msgDiv = document.createElement('div');
                msgDiv.className = 'message bot';
                msgDiv.innerHTML = renderAllMarkdown(textContent);
                chatWindow.appendChild(msgDiv);
                chatWindow.scrollTop = chatWindow.scrollHeight;
            }
        } else {
            appendMessage(data.result || JSON.stringify(data), 'bot');
        }
    } catch (err) {
        chatWindow.lastChild.remove();
        appendMessage('Error: ' + err.message, 'bot');
    }
});