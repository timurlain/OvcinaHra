window.ovcinaSw = {
    unregisterAll: async () => {
        if (!("serviceWorker" in navigator)) {
            return {
                supported: false,
                count: 0,
                successful: 0,
                failed: 0,
                detail: "serviceWorker not supported"
            };
        }

        let registrations;
        try {
            registrations = await navigator.serviceWorker.getRegistrations();
        } catch (error) {
            return {
                supported: true,
                count: 0,
                successful: 0,
                failed: 1,
                detail: error?.message ?? String(error)
            };
        }

        let successful = 0;
        const errors = [];
        await Promise.all(registrations.map(async (registration, index) => {
            try {
                if (await registration.unregister()) {
                    successful += 1;
                } else {
                    errors.push(`registration ${index} returned false`);
                }
            } catch (error) {
                errors.push(error?.message ?? String(error));
            }
        }));

        return {
            supported: true,
            count: registrations.length,
            successful: successful,
            failed: registrations.length - successful,
            detail: errors.join("; ")
        };
    }
};
