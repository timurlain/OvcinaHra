export function attachEnterToSend(textarea, dotNetRef) {
    const textareaPresent = Boolean(textarea);
    logChatInput(`attached textareaPresent=${textareaPresent}`);

    if (!textarea) {
        return false;
    }

    if (textarea._ohBotEnterHandler) {
        textarea.removeEventListener('keydown', textarea._ohBotEnterHandler, true);
    }

    let sending = false;
    const handler = async (event) => {
        if (event.key !== 'Enter') {
            return;
        }

        logChatInput(`keydown key=Enter shift=${flag(event.shiftKey)} composing=${flag(event.isComposing)}`);

        if (event.isComposing) {
            return;
        }

        if (event.shiftKey) {
            logChatInput('newline-via-shift-enter');
            return;
        }

        if (event.altKey || event.ctrlKey || event.metaKey) {
            return;
        }

        event.preventDefault();

        if (event.repeat
            || sending
            || !textarea.value
            || textarea.value.trim().length === 0) {
            return;
        }

        logChatInput('submit-via-enter');
        sending = true;
        try {
            await dotNetRef.invokeMethodAsync('SendDraftFromKeyboardAsync');
        } catch (error) {
            logChatInput('submit-via-enter failed');
            console.warn('Bot consult drawer Enter-to-send failed.', error);
        } finally {
            sending = false;
        }
    };

    textarea._ohBotEnterHandler = handler;
    textarea.addEventListener('keydown', handler, { capture: true });
    return true;
}

function logChatInput(message) {
    console.debug(`[chat-input] ${message}`);
}

function flag(value) {
    return value ? 't' : 'f';
}
