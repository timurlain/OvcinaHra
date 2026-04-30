window.ovcinaVersionRefresh = {
    unregisterServiceWorkers: async () => {
        if (!("serviceWorker" in navigator)) {
            return {
                Supported: false,
                Count: 0,
                Successful: 0,
                Failed: 0,
                Detail: "serviceWorker not supported"
            };
        }

        let registrations;
        try {
            registrations = await navigator.serviceWorker.getRegistrations();
        } catch (error) {
            return {
                Supported: true,
                Count: 0,
                Successful: 0,
                Failed: 1,
                Detail: error?.message ?? String(error)
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
            Supported: true,
            Count: registrations.length,
            Successful: successful,
            Failed: registrations.length - successful,
            Detail: errors.join("; ")
        };
    }
};
