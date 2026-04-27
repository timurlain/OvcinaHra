export function attachEnterToSend(textarea, dotNetRef) {
    if (!textarea) {
        return;
    }

    if (textarea._ohBotEnterHandler) {
        textarea.removeEventListener('keydown', textarea._ohBotEnterHandler);
    }

    let sending = false;
    const handler = async (event) => {
        if (event.key !== 'Enter'
            || event.shiftKey
            || event.altKey
            || event.ctrlKey
            || event.metaKey
            || event.isComposing) {
            return;
        }

        event.preventDefault();

        if (event.repeat
            || sending
            || !textarea.value
            || textarea.value.trim().length === 0) {
            return;
        }

        sending = true;
        try {
            await dotNetRef.invokeMethodAsync('SendDraftFromKeyboardAsync');
        } catch (error) {
            console.warn('Bot consult drawer Enter-to-send failed.', error);
        } finally {
            sending = false;
        }
    };

    textarea._ohBotEnterHandler = handler;
    textarea.addEventListener('keydown', handler);
}
