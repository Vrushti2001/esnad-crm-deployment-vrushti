// new_/AutoCloseOnClosure.js
var AutoCloseOnClosure = AutoCloseOnClosure || {};

(function () {
    /**
     * Logs messages to the console with a consistent prefix.
     */
    function log(level, message) {
        var prefix = "[AutoCloseOnClosure][" + level + "] ";
        if (level === "ERROR") console.error(prefix + message);
        else if (level === "WARN") console.warn(prefix + message);
        else console.log(prefix + message);
    }

    /**
     * Form OnLoad: Start polling for BPF stage changes.
     */
    AutoCloseOnClosure.onLoad = function (executionContext) {
        if (!executionContext || !executionContext.getFormContext) {
            log("ERROR", "Execution context not passed. Check event registration and ensure 'Pass execution context' is checked.");
            return;
        }

        var formContext = executionContext.getFormContext();

        // Ensure BPF process is available
        var process = formContext.data.process;
        if (!process) {
            log("ERROR", "No BPF process available on form.");
            return;
        }

        try {
            var lastStage = process.getActiveStage();
            if (lastStage) {
                log("INFO", "Polling started. Initial stage: " + lastStage.getName());
            } else {
                log("WARN", "Polling started, but no active stage found.");
            }

            // Poll every 2 seconds
            var poller = setInterval(function () {
                // Stop polling if form is closed or invalid
                if (!formContext.data || !formContext.data.isValid()) {
                    log("WARN", "Form context is invalid. Stopping poller.");
                    clearInterval(poller);
                    return;
                }

                var activeStage = process.getActiveStage();
                if (!activeStage) return;

                // Detect stage change
                if (!lastStage || activeStage.getId() !== lastStage.getId()) {
                    lastStage = activeStage;
                    log("INFO", "BPF moved to: " + activeStage.getName());

                    // If moved to Ticket Closure
                    if (activeStage.getName().toLowerCase() === "ticket closure") {
                        clearInterval(poller);
                        log("INFO", "Detected Ticket Closure → auto-closing case.");
                        AutoCloseOnClosure.closeCaseViaWebApi(formContext, formContext.data.entity.getId(), 5); // 5 = Resolved status
                    }
                }
            }, 2000);
        } catch (e) {
            log("ERROR", "onLoad exception: " + (e.message || e));
        }
    };

    /**
     * Closes the Case via the IncidentClose Web API action.
     * @param {object} formContext - Current form context
     * @param {string} caseId - Case GUID
     * @param {number} statusReason - Status reason code (e.g., 5 for Resolved)
     */
    AutoCloseOnClosure.closeCaseViaWebApi = function (formContext, caseId, statusReason) {
        try {
            if (!caseId) {
                log("ERROR", "Case ID is missing. Cannot close case.");
                return;
            }

            caseId = caseId.replace(/[{}]/g, "").toLowerCase();

            var resolution = {
                "@odata.type": "Microsoft.Dynamics.CRM.incidentresolution",
                subject: "Auto-closed on Ticket Closure",
                description: "Automatically closed when BPF reached Ticket Closure stage.",
                "incidentid@odata.bind": "/incidents(" + caseId + ")"
            };

            var request = {
                entityName: "incident",
                operationName: "CloseIncident",
                boundParameter: "entity",
                parameters: {
                    IncidentResolution: resolution,
                    Status: statusReason
                },
                getMetadata: function () {
                    return {
                        boundParameter: "entity",
                        parameterTypes: {
                            "IncidentResolution": { typeName: "Microsoft.Dynamics.CRM.incidentresolution", structuralProperty: 5 },
                            "Status": { typeName: "Edm.Int32", structuralProperty: 1 }
                        },
                        operationType: 0, // Action
                        operationName: "CloseIncident"
                    };
                }
            };

            Xrm.WebApi.online.execute(request).then(
                function () {
                    log("INFO", "Case auto-closed successfully.");
                    formContext.data.refresh(true);
                },
                function (error) {
                    log("ERROR", "Auto-close failed: " + (error.message || JSON.stringify(error)));
                }
            );
        } catch (e) {
            log("ERROR", "closeCaseViaWebApi exception: " + (e.message || e));
        }
    };
})();
